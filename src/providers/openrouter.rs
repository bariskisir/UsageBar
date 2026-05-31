use async_trait::async_trait;
use reqwest::header::{HeaderValue, AUTHORIZATION};
use rust_decimal::Decimal;

use crate::domain::{IUsageProvider, ProviderCredentials, ProviderResult, UsageBlock};
use crate::providers::json_helpers;

pub struct OpenRouterProvider {
    http_client: reqwest::Client,
}

impl OpenRouterProvider {
    pub fn new(http_client: reqwest::Client) -> Self {
        Self { http_client }
    }
}

#[async_trait]
impl IUsageProvider for OpenRouterProvider {
    fn name(&self) -> &str {
        "OpenRouter"
    }

    async fn get_usage(
        &self,
        credentials: &ProviderCredentials,
        cancellation_token: tokio_util::sync::CancellationToken,
    ) -> anyhow::Result<Option<ProviderResult>> {
        let api_key = credentials.openrouter_api_key.trim();
        if api_key.is_empty() {
            return Ok(None);
        }

        let response = tokio::select! {
            r = self.http_client
                .get("https://openrouter.ai/api/v1/credits")
                .header(AUTHORIZATION, HeaderValue::from_str(&format!("Bearer {}", api_key))?)
                .send() => r?,
            _ = cancellation_token.cancelled() => {
                anyhow::bail!("OpenRouter request cancelled.");
            }
        };

        let status = response.status();
        if !status.is_success() {
            anyhow::bail!("OpenRouter returned HTTP {}", status);
        }

        let body: serde_json::Value = response.json().await?;

        let data = json_helpers::try_get_property(&body, "data")
            .ok_or_else(|| anyhow::anyhow!("OpenRouter response did not contain data."))?;

        let total_credits = json_helpers::get_decimal(data, "total_credits").ok_or_else(|| {
            anyhow::anyhow!("OpenRouter response did not contain total_credits.")
        })?;
        let total_usage = json_helpers::get_decimal(data, "total_usage").ok_or_else(|| {
            anyhow::anyhow!("OpenRouter response did not contain total_usage.")
        })?;

        let remaining = total_credits - total_usage;

        Ok(Some(ProviderResult {
            blocks: vec![UsageBlock {
                label: "OpenRouter:".to_string(),
                value: format_currency(remaining),
                inline: true,
            }],
            codex_primary_used_percent: None,
            codex_secondary_used_percent: None,
        }))
    }
}

fn format_currency(value: Decimal) -> String {
    format!("${:.2}", value)
}
