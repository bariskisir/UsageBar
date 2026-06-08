use std::{fs, path::PathBuf};

use async_trait::async_trait;
use chrono::{DateTime, Utc};
use reqwest::Client;
use serde_json::Value;

use crate::{
    config::AppPaths,
    domain::{ProviderCategory, ProviderResult, UsageWindow},
    providers::{
        UsageProvider,
        context::ProviderQueryContext,
        error::{ProviderResultValue, ensure_success, invalid},
        formatting::{capitalize, reset_duration},
        json::{get_f64, get_string},
    },
};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ClaudeAuth {
    pub access_token: String,
    pub subscription_type: Option<String>,
    pub rate_limit_tier: Option<String>,
}

#[derive(Debug, Clone)]
pub struct ClaudeAuthReader {
    path: PathBuf,
}

impl Default for ClaudeAuthReader {
    fn default() -> Self {
        let path = AppPaths::user_profile()
            .unwrap_or_default()
            .join(".claude")
            .join(".credentials.json");
        Self { path }
    }
}

impl ClaudeAuthReader {
    pub fn new(path: PathBuf) -> Self {
        Self { path }
    }

    pub fn read(&self) -> Option<ClaudeAuth> {
        let raw = fs::read_to_string(&self.path).ok()?;
        let root: Value = serde_json::from_str(&raw).ok()?;
        let oauth = root.get("claudeAiOauth")?;
        let access_token = get_string(oauth, &["accessToken"])?;
        if access_token.trim().is_empty() {
            return None;
        }
        Some(ClaudeAuth {
            access_token,
            subscription_type: get_string(oauth, &["subscriptionType"]),
            rate_limit_tier: get_string(oauth, &["rateLimitTier"]),
        })
    }
}

#[derive(Debug, Clone)]
pub struct ClaudeProvider {
    client: Client,
    auth_reader: ClaudeAuthReader,
    endpoint: String,
}

impl ClaudeProvider {
    pub fn new(client: Client, auth_reader: ClaudeAuthReader) -> Self {
        Self {
            client,
            auth_reader,
            endpoint: "https://api.anthropic.com/api/oauth/usage".to_string(),
        }
    }

    pub fn with_endpoint(
        client: Client,
        auth_reader: ClaudeAuthReader,
        endpoint: impl Into<String>,
    ) -> Self {
        Self {
            client,
            auth_reader,
            endpoint: endpoint.into(),
        }
    }
}

#[async_trait]
impl UsageProvider for ClaudeProvider {
    fn name(&self) -> &'static str {
        "Claude"
    }

    fn category(&self) -> ProviderCategory {
        ProviderCategory::Metric
    }

    async fn get_usage(&self, context: &ProviderQueryContext) -> ProviderResultValue {
        let Some(auth) = self.auth_reader.read() else {
            return Ok(None);
        };
        let plan_source = auth
            .subscription_type
            .as_deref()
            .or(auth.rate_limit_tier.as_deref());
        let plan = plan_source.map(plan_label);

        let response = self
            .client
            .get(&self.endpoint)
            .bearer_auth(auth.access_token)
            .header("accept", "application/json")
            .header("anthropic-beta", "oauth-2025-04-20")
            .header("User-Agent", "claude-code/2.1.0")
            .send()
            .await?;
        ensure_success("Claude", response.status())?;
        let root: Value = response.json().await?;

        let mut windows = Vec::new();
        if let Some(window) = read_window(&root, "five_hour", "Session", context.now) {
            windows.push(window);
        }
        if let Some(window) = read_window(&root, "seven_day", "Weekly", context.now) {
            windows.push(window);
        }
        if windows.is_empty() {
            return Err(invalid(
                "Claude",
                "response did not contain usable rate limit windows",
            ));
        }
        Ok(Some(ProviderResult::metric("Claude", plan, windows)))
    }
}

fn read_window(
    root: &Value,
    property: &str,
    label: &str,
    now: DateTime<Utc>,
) -> Option<UsageWindow> {
    let window = root.get(property)?;
    let utilization = get_f64(window, "utilization")?;
    let reset_text = get_string(window, &["resets_at"])
        .and_then(|raw| DateTime::parse_from_rfc3339(&raw).ok())
        .map(|reset| reset_duration(reset.with_timezone(&Utc) - now));
    Some(UsageWindow::new("Claude", label, utilization, reset_text))
}

fn plan_label(tier: &str) -> String {
    let normalized = tier.trim().to_ascii_lowercase();
    if normalized.contains("max") {
        "Max".to_string()
    } else if normalized.contains("pro") {
        "Pro".to_string()
    } else if normalized.contains("team") {
        "Team".to_string()
    } else if normalized.contains("enterprise") {
        "Enterprise".to_string()
    } else if normalized.contains("free") {
        "Free".to_string()
    } else if normalized == "default_claude_ai" {
        "Claude AI".to_string()
    } else {
        capitalize(&normalized)
    }
}
