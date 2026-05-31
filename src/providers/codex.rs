use async_trait::async_trait;
use chrono::{DateTime, Local, TimeZone, Utc};
use reqwest::header::{HeaderMap, HeaderValue, ACCEPT, AUTHORIZATION};
use serde_json::Value;

use crate::domain::{IUsageProvider, ProviderCredentials, ProviderResult, UsageBlock};
use crate::providers::json_helpers;

const USAGE_ENDPOINT: &str = "https://chatgpt.com/backend-api/wham/usage";

pub struct CodexProvider {
    http_client: reqwest::Client,
}

impl CodexProvider {
    pub fn new(http_client: reqwest::Client) -> Self {
        Self { http_client }
    }
}

#[async_trait]
impl IUsageProvider for CodexProvider {
    fn name(&self) -> &str {
        "Codex"
    }

    async fn get_usage(
        &self,
        _credentials: &ProviderCredentials,
        cancellation_token: tokio_util::sync::CancellationToken,
    ) -> anyhow::Result<Option<ProviderResult>> {
        let auth = match read_auth() {
            Some(a) => a,
            None => return Ok(None),
        };

        let mut headers = HeaderMap::new();
        headers.insert(ACCEPT, HeaderValue::from_static("application/json"));
        headers.insert(
            AUTHORIZATION,
            HeaderValue::from_str(&format!("Bearer {}", auth.access_token))?,
        );
        headers.insert("originator", HeaderValue::from_static("codex_cli_rs"));
        headers.insert("chatgpt-account-id", HeaderValue::from_str(&auth.account_id)?);

        let response = tokio::select! {
            r = self.http_client.get(USAGE_ENDPOINT).headers(headers).send() => r?,
            _ = cancellation_token.cancelled() => {
                anyhow::bail!("Codex request cancelled.");
            }
        };

        let status = response.status();
        if !status.is_success() {
            anyhow::bail!("Codex returned HTTP {}", status);
        }

        let body: Value = response.json().await?;
        let rate_limit = get_rate_limit(&body)?;
        let primary = get_window(&rate_limit, "primary_window");
        let secondary = get_window(&rate_limit, "secondary_window");

        let mut blocks = Vec::new();
        if let Some(ref pw) = primary {
            blocks.push(UsageBlock {
                label: "Codex 5h:".to_string(),
                value: format_usage_line(pw),
                inline: true,
            });
        }
        if let Some(ref sw) = secondary {
            blocks.push(UsageBlock {
                label: "Codex 7d:".to_string(),
                value: format_usage_line(sw),
                inline: true,
            });
        }

        if blocks.is_empty() {
            anyhow::bail!("Codex response did not contain usable rate limit windows.");
        }

        Ok(Some(ProviderResult {
            blocks,
            codex_primary_used_percent: primary.map(|w| w.used_percent),
            codex_secondary_used_percent: secondary.map(|w| w.used_percent),
        }))
    }
}

// ---------------------------------------------------------------------------
// Auth
// ---------------------------------------------------------------------------

struct CodexAuth {
    access_token: String,
    account_id: String,
}

fn read_auth() -> Option<CodexAuth> {
    let user_profile = std::env::var("USERPROFILE").ok()?;
    let auth_path = std::path::PathBuf::from(&user_profile)
        .join(".codex")
        .join("auth.json");

    let json_text = std::fs::read_to_string(&auth_path).ok()?;
    let root: Value = serde_json::from_str(&json_text).ok()?;

    let token_source = json_helpers::try_get_property(&root, "tokens").unwrap_or(&root);
    let access_token = json_helpers::get_string(token_source, &["access_token"])?;
    let account_id = json_helpers::get_string(token_source, &["account_id"])?;

    if access_token.trim().is_empty() || account_id.trim().is_empty() {
        return None;
    }

    Some(CodexAuth {
        access_token,
        account_id,
    })
}

// ---------------------------------------------------------------------------
// JSON traversal
// ---------------------------------------------------------------------------

fn get_rate_limit(root: &Value) -> anyhow::Result<Value> {
    // Prefer top-level "rate_limit".
    if let Some(rl) = json_helpers::try_get_property(root, "rate_limit") {
        if rl.is_object() {
            return Ok(rl.clone());
        }
    }

    // Fall back to the first nested rate_limit inside "additional_rate_limits".
    if let Some(arr) = json_helpers::try_get_property(root, "additional_rate_limits") {
        if let Some(items) = arr.as_array() {
            for item in items {
                if let Some(nested) = json_helpers::try_get_property(item, "rate_limit") {
                    if nested.is_object() {
                        return Ok(nested.clone());
                    }
                }
            }
        }
    }

    anyhow::bail!("Codex response did not contain rate_limit.");
}

#[derive(Debug, Clone)]
struct CodexWindow {
    used_percent: f64,
    reset_at: DateTime<Local>,
}

fn get_window(rate_limit: &Value, property_name: &str) -> Option<CodexWindow> {
    let window = json_helpers::try_get_property(rate_limit, property_name)?;
    if !window.is_object() {
        return None;
    }

    let used_percent = json_helpers::get_double(window, "used_percent")?;
    let reset_at_epoch = json_helpers::get_double(window, "reset_at")?;

    let used_percent = used_percent.clamp(0.0, 100.0);

    Some(CodexWindow {
        used_percent,
        reset_at: from_epoch(reset_at_epoch),
    })
}

fn from_epoch(epoch: f64) -> DateTime<Local> {
    let seconds = if epoch > 10_000_000_000.0 {
        epoch / 1000.0
    } else {
        epoch
    };
    Utc.timestamp_opt(seconds as i64, 0)
        .single()
        .unwrap_or_else(|| Utc.timestamp_opt(0, 0).single().unwrap())
        .with_timezone(&Local)
}

// ---------------------------------------------------------------------------
// Formatting
// ---------------------------------------------------------------------------

fn format_usage_line(window: &CodexWindow) -> String {
    let duration = window.reset_at - Local::now();
    format!(
        "{:.1}%, resets in {}",
        window.used_percent,
        format_reset_duration(duration)
    )
}

fn format_reset_duration(duration: chrono::Duration) -> String {
    if duration <= chrono::Duration::zero() {
        return "now".to_string();
    }

    if duration >= chrono::Duration::days(1) {
        let total_minutes = (duration.num_seconds() as f64 / 60.0).ceil() as i64;
        let d = total_minutes / (60 * 24);
        let h = (total_minutes % (60 * 24)) / 60;
        return format!("{}d {}h", d, h);
    }

    let total_minutes = std::cmp::max(1, (duration.num_seconds() as f64 / 60.0).ceil() as i64);
    let hours = total_minutes / 60;
    let minutes = total_minutes % 60;

    if hours >= 1 {
        format!("{}h {}m", hours, minutes)
    } else {
        format!("{}m", minutes)
    }
}
