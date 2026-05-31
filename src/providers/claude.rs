use async_trait::async_trait;
use chrono::{DateTime, Local};
use reqwest::header::{HeaderMap, HeaderValue, ACCEPT, AUTHORIZATION};
use serde_json::Value;

use crate::domain::{IUsageProvider, ProviderCredentials, ProviderResult, UsageBarWindow, UsageBlock};
use crate::providers::json_helpers;

const USAGE_ENDPOINT: &str = "https://api.anthropic.com/api/oauth/usage";
const BETA_HEADER: &str = "oauth-2025-04-20";
const USER_AGENT: &str = "claude-code/2.1.0";

pub struct ClaudeProvider {
    http_client: reqwest::Client,
}

impl ClaudeProvider {
    pub fn new(http_client: reqwest::Client) -> Self {
        Self { http_client }
    }
}

#[async_trait]
impl IUsageProvider for ClaudeProvider {
    fn name(&self) -> &str {
        "Claude"
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
        headers.insert(
            "anthropic-beta",
            HeaderValue::from_static(BETA_HEADER),
        );
        headers.insert(
            "User-Agent",
            HeaderValue::from_static(USER_AGENT),
        );

        let response = tokio::select! {
            r = self.http_client.get(USAGE_ENDPOINT).headers(headers).send() => r?,
            _ = cancellation_token.cancelled() => {
                anyhow::bail!("Claude request cancelled.");
            }
        };

        let status = response.status();
        if !status.is_success() {
            anyhow::bail!("Claude returned HTTP {}", status);
        }

        let body: Value = response.json().await?;
        let five_hour = get_window(&body, "five_hour");
        let seven_day = get_window(&body, "seven_day");

        let mut blocks = Vec::new();
        let mut windows = Vec::new();

        if let Some(ref w) = five_hour {
            blocks.push(UsageBlock {
                label: "Claude 5h:".to_string(),
                value: format_usage_line(w),
                inline: true,
            });
            windows.push(UsageBarWindow {
                provider_name: "Claude".to_string(),
                window_label: "5h".to_string(),
                used_percent: w.used_percent.clamp(0.0, 100.0),
            });
        }

        if let Some(ref w) = seven_day {
            blocks.push(UsageBlock {
                label: "Claude 7d:".to_string(),
                value: format_usage_line(w),
                inline: true,
            });
            windows.push(UsageBarWindow {
                provider_name: "Claude".to_string(),
                window_label: "7d".to_string(),
                used_percent: w.used_percent.clamp(0.0, 100.0),
            });
        }

        if blocks.is_empty() {
            anyhow::bail!("Claude response did not contain usable rate limit windows.");
        }

        Ok(Some(ProviderResult { blocks, windows }))
    }
}

// ---------------------------------------------------------------------------
// Auth
// ---------------------------------------------------------------------------

struct ClaudeAuth {
    access_token: String,
}

fn read_auth() -> Option<ClaudeAuth> {
    let user_profile = std::env::var("USERPROFILE").ok()?;
    let auth_path = std::path::PathBuf::from(&user_profile)
        .join(".claude")
        .join(".credentials.json");

    let json_text = std::fs::read_to_string(&auth_path).ok()?;
    let root: Value = serde_json::from_str(&json_text).ok()?;

    let oauth = json_helpers::try_get_property(&root, "claudeAiOauth")?;
    let access_token = json_helpers::get_string(oauth, &["accessToken"])?;

    if access_token.trim().is_empty() {
        return None;
    }

    Some(ClaudeAuth { access_token })
}

// ---------------------------------------------------------------------------
// JSON parsing
// ---------------------------------------------------------------------------

struct ClaudeWindow {
    used_percent: f64,
    reset_at: Option<DateTime<Local>>,
}

fn get_window(root: &Value, property_name: &str) -> Option<ClaudeWindow> {
    let window = json_helpers::try_get_property(root, property_name)?;
    if !window.is_object() {
        return None;
    }

    let utilization = json_helpers::get_double(window, "utilization")?;
    let resets_at = json_helpers::get_string(window, &["resets_at"]);
    let reset_time = parse_reset_time(resets_at.as_deref());

    Some(ClaudeWindow {
        used_percent: utilization * 100.0,
        reset_at: reset_time,
    })
}

fn parse_reset_time(iso8601: Option<&str>) -> Option<DateTime<Local>> {
    let s = iso8601?;
    if s.trim().is_empty() {
        return None;
    }

    // Try RFC 3339 / ISO 8601 parsing.
    if let Ok(dt) = chrono::DateTime::parse_from_rfc3339(s) {
        return Some(dt.with_timezone(&Local));
    }

    None
}

// ---------------------------------------------------------------------------
// Formatting
// ---------------------------------------------------------------------------

fn format_usage_line(window: &ClaudeWindow) -> String {
    let percent = format_used_percent(window.used_percent);
    match window.reset_at {
        Some(reset) => {
            let duration = reset - Local::now();
            format!("{}, {}", percent, format_reset_duration(duration))
        }
        None => percent,
    }
}

fn format_used_percent(used_percent: f64) -> String {
    let value = format!("{:.1}", used_percent);
    value
        .strip_suffix(".0")
        .unwrap_or(value.as_str())
        .to_string()
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
