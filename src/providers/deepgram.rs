use async_trait::async_trait;
use reqwest::header::{HeaderMap, HeaderValue, ACCEPT};
use rust_decimal::Decimal;
use serde_json::Value;

use crate::domain::{IUsageProvider, ProviderCredentials, ProviderResult, UsageBlock};
use crate::providers::json_helpers;

pub struct DeepgramProvider {
    http_client: reqwest::Client,
}

impl DeepgramProvider {
    pub fn new(http_client: reqwest::Client) -> Self {
        Self { http_client }
    }
}

#[async_trait]
impl IUsageProvider for DeepgramProvider {
    fn name(&self) -> &str {
        "Deepgram"
    }

    async fn get_usage(
        &self,
        credentials: &ProviderCredentials,
        cancellation_token: tokio_util::sync::CancellationToken,
    ) -> anyhow::Result<Option<ProviderResult>> {
        let api_key = credentials.deepgram_api_key.trim();
        if api_key.is_empty() {
            return Ok(None);
        }

        let project_id = get_first_project_id(&self.http_client, api_key, &cancellation_token).await?;
        let balance = get_balance(&self.http_client, api_key, &project_id, &cancellation_token).await?;

        Ok(Some(ProviderResult {
            blocks: vec![UsageBlock {
                label: "Deepgram:".to_string(),
                value: format_currency(balance),
                inline: true,
            }],
            windows: Vec::new(),
        }))
    }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

fn auth_headers(api_key: &str) -> anyhow::Result<HeaderMap> {
    let mut headers = HeaderMap::new();
    headers.insert(
        "Authorization",
        HeaderValue::from_str(&format!("Token {}", api_key))?,
    );
    headers.insert(ACCEPT, HeaderValue::from_static("application/json"));
    Ok(headers)
}

async fn get_first_project_id(
    client: &reqwest::Client,
    api_key: &str,
    ct: &tokio_util::sync::CancellationToken,
) -> anyhow::Result<String> {
    let response = tokio::select! {
        r = client
            .get("https://api.deepgram.com/v1/projects")
            .headers(auth_headers(api_key)?)
            .send() => r?,
        _ = ct.cancelled() => {
            anyhow::bail!("Deepgram request cancelled.");
        }
    };

    let status = response.status();
    if !status.is_success() {
        anyhow::bail!("Deepgram returned HTTP {}", status);
    }

    let body: Value = response.json().await?;

    for project in enumerate_projects(&body) {
        if let Some(id) = json_helpers::get_string(project, &["project_id", "projectId", "id"]) {
            if !id.trim().is_empty() {
                return Ok(id);
            }
        }
    }

    anyhow::bail!("Deepgram response did not contain a project_id.");
}

async fn get_balance(
    client: &reqwest::Client,
    api_key: &str,
    project_id: &str,
    ct: &tokio_util::sync::CancellationToken,
) -> anyhow::Result<Decimal> {
    let url = format!(
        "https://api.deepgram.com/v1/projects/{}/balances",
        urlencoding::encode(project_id)
    );

    let response = tokio::select! {
        r = client
            .get(&url)
            .headers(auth_headers(api_key)?)
            .send() => r?,
        _ = ct.cancelled() => {
            anyhow::bail!("Deepgram request cancelled.");
        }
    };

    let status = response.status();
    if !status.is_success() {
        anyhow::bail!("Deepgram returned HTTP {}", status);
    }

    let body: Value = response.json().await?;

    let mut total = Decimal::ZERO;
    let mut found = false;

    for balance in enumerate_balances(&body) {
        let amount = json_helpers::get_decimal(balance, "amount")
            .or_else(|| json_helpers::get_decimal(balance, "balance"))
            .or_else(|| json_helpers::get_decimal(balance, "total_balance"));

        let Some(amount) = amount else { continue };

        let units = json_helpers::get_string(balance, &["units", "currency"]);
        if let Some(ref u) = units {
            if !u.eq_ignore_ascii_case("usd") && !u.trim().is_empty() {
                continue;
            }
        }

        total += amount;
        found = true;
    }

    if !found {
        anyhow::bail!("Deepgram response did not contain a balance amount.");
    }

    Ok(total)
}

fn enumerate_projects(root: &Value) -> Box<dyn Iterator<Item = &Value> + '_> {
    if let Some(arr) = root.as_array() {
        return Box::new(arr.iter());
    }

    if let Some(projects) = json_helpers::try_get_property(root, "projects") {
        if let Some(arr) = projects.as_array() {
            return Box::new(arr.iter());
        }
    }

    Box::new(std::iter::empty())
}

fn enumerate_balances(root: &Value) -> Box<dyn Iterator<Item = &Value> + '_> {
    if let Some(arr) = root.as_array() {
        return Box::new(arr.iter());
    }

    if let Some(balances) = json_helpers::try_get_property(root, "balances") {
        if let Some(arr) = balances.as_array() {
            return Box::new(arr.iter());
        }
        // The C# code falls through to yield the root element if balances exists
        // but isn't an array.  We're more conservative here — skip it.
    }

    // Final fallback: the root itself (matching C# behavior for single-balance
    // responses).
    Box::new(std::iter::once(root))
}

fn format_currency(value: Decimal) -> String {
    format!("${:.2}", value)
}
