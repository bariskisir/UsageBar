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
        error::{ProviderError, ProviderResultValue, ensure_success, invalid},
        formatting::{capitalize, reset_duration},
        json::{get_f64, get_string},
    },
};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CodexAuth {
    pub access_token: String,
    pub account_id: String,
}

#[derive(Debug, Clone)]
pub struct CodexAuthReader {
    path: PathBuf,
}

impl Default for CodexAuthReader {
    fn default() -> Self {
        let path = AppPaths::user_profile()
            .unwrap_or_default()
            .join(".codex")
            .join("auth.json");
        Self { path }
    }
}

impl CodexAuthReader {
    pub fn new(path: PathBuf) -> Self {
        Self { path }
    }

    pub fn read(&self) -> Option<CodexAuth> {
        let raw = fs::read_to_string(&self.path).ok()?;
        let root: Value = serde_json::from_str(&raw).ok()?;
        let source = root.get("tokens").unwrap_or(&root);
        let access_token = get_string(source, &["access_token"])?;
        let account_id = get_string(source, &["account_id"])?;
        if access_token.trim().is_empty() || account_id.trim().is_empty() {
            return None;
        }
        Some(CodexAuth {
            access_token,
            account_id,
        })
    }
}

#[derive(Debug, Clone)]
pub struct CodexProvider {
    client: Client,
    auth_reader: CodexAuthReader,
    endpoint: String,
}

impl CodexProvider {
    pub fn new(client: Client, auth_reader: CodexAuthReader) -> Self {
        Self {
            client,
            auth_reader,
            endpoint: "https://chatgpt.com/backend-api/wham/usage".to_string(),
        }
    }

    pub fn with_endpoint(
        client: Client,
        auth_reader: CodexAuthReader,
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
impl UsageProvider for CodexProvider {
    fn name(&self) -> &'static str {
        "Codex"
    }

    fn category(&self) -> ProviderCategory {
        ProviderCategory::Metric
    }

    async fn get_usage(&self, context: &ProviderQueryContext) -> ProviderResultValue {
        let Some(auth) = self.auth_reader.read() else {
            return Ok(None);
        };

        let response = self
            .client
            .get(&self.endpoint)
            .bearer_auth(auth.access_token)
            .header("accept", "application/json")
            .header("originator", "codex_cli_rs")
            .header("chatgpt-account-id", auth.account_id)
            .send()
            .await?;
        ensure_success("Codex", response.status())?;
        let root: Value = response.json().await?;

        let plan = get_string(&root, &["plan_type"]).map(|value| plan_label(&value));
        let rate_limit = rate_limit(&root)?;
        let mut windows = Vec::new();
        if let Some(window) = read_window(rate_limit, "primary_window", "Session", context.now) {
            windows.push(window);
        }
        if let Some(window) = read_window(rate_limit, "secondary_window", "Weekly", context.now) {
            windows.push(window);
        }
        if windows.is_empty() {
            return Err(invalid(
                "Codex",
                "response did not contain usable rate limit windows",
            ));
        }
        Ok(Some(ProviderResult::metric("Codex", plan, windows)))
    }
}

fn rate_limit(root: &Value) -> Result<&Value, ProviderError> {
    if let Some(rate_limit) = root.get("rate_limit").filter(|value| value.is_object()) {
        return Ok(rate_limit);
    }
    if let Some(items) = root.get("additional_rate_limits").and_then(Value::as_array) {
        for item in items {
            if let Some(rate_limit) = item.get("rate_limit").filter(|value| value.is_object()) {
                return Ok(rate_limit);
            }
        }
    }
    Err(invalid("Codex", "response did not contain rate_limit"))
}

fn read_window(
    rate_limit: &Value,
    property: &str,
    label: &str,
    now: DateTime<Utc>,
) -> Option<UsageWindow> {
    let window = rate_limit.get(property)?;
    let used_percent = get_f64(window, "used_percent")?;
    let reset_at = get_f64(window, "reset_at")?;
    let seconds = if reset_at > 10_000_000_000.0 {
        reset_at / 1000.0
    } else {
        reset_at
    };
    let reset = DateTime::<Utc>::from_timestamp(seconds.round() as i64, 0)?;
    Some(UsageWindow::new(
        "Codex",
        label,
        used_percent,
        Some(reset_duration(reset - now)),
    ))
}

fn plan_label(plan_type: &str) -> String {
    match plan_type.trim().to_ascii_lowercase().as_str() {
        "free" => "Free".to_string(),
        "plus" => "Plus".to_string(),
        "pro" => "Pro".to_string(),
        "pro_lite" | "prolite" | "pro-lite" => "Pro Lite".to_string(),
        "go" => "Go".to_string(),
        "team" => "Team".to_string(),
        "business" => "Business".to_string(),
        "enterprise" => "Enterprise".to_string(),
        "education" | "edu" => "Education".to_string(),
        "guest" => "Guest".to_string(),
        other => capitalize(other),
    }
}
