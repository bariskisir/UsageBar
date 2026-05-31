use async_trait::async_trait;
use reqwest::header::{HeaderValue, AUTHORIZATION};
use rust_decimal::Decimal;

use crate::domain::{IUsageProvider, ProviderCredentials, ProviderResult, UsageBlock};
use crate::providers::json_helpers;

pub struct DeepSeekProvider {
    http_client: reqwest::Client,
}

impl DeepSeekProvider {
    pub fn new(http_client: reqwest::Client) -> Self {
        Self { http_client }
    }
}

#[async_trait]
impl IUsageProvider for DeepSeekProvider {
    fn name(&self) -> &str {
        "DeepSeek"
    }

    async fn get_usage(
        &self,
        credentials: &ProviderCredentials,
        cancellation_token: tokio_util::sync::CancellationToken,
    ) -> anyhow::Result<Option<ProviderResult>> {
        let api_key = credentials.deepseek_api_key.trim();
        if api_key.is_empty() {
            return Ok(None);
        }

        let response = tokio::select! {
            r = self.http_client
                .get("https://api.deepseek.com/user/balance")
                .header(AUTHORIZATION, HeaderValue::from_str(&format!("Bearer {}", api_key))?)
                .send() => r?,
            _ = cancellation_token.cancelled() => {
                anyhow::bail!("DeepSeek request cancelled.");
            }
        };

        let status = response.status();
        if !status.is_success() {
            anyhow::bail!("DeepSeek returned HTTP {}", status);
        }

        let body: serde_json::Value = response.json().await?;

        let balance_infos = json_helpers::try_get_property(&body, "balance_infos")
            .and_then(|v| v.as_array())
            .ok_or_else(|| {
                anyhow::anyhow!("DeepSeek response did not contain balance_infos.")
            })?;

        for info in balance_infos {
            let currency = json_helpers::get_string(info, &["currency"]);
            if !matches!(currency.as_deref(), Some("USD")) {
                continue;
            }

            let total_balance = json_helpers::get_decimal(info, "total_balance").ok_or_else(|| {
                anyhow::anyhow!("DeepSeek USD balance did not contain total_balance.")
            })?;

            return Ok(Some(ProviderResult {
                blocks: vec![UsageBlock {
                    label: "DeepSeek:".to_string(),
                    value: format_currency(total_balance),
                    inline: true,
                }],
                windows: Vec::new(),
            }));
        }

        anyhow::bail!("DeepSeek response did not contain a USD balance.");
    }
}

fn format_currency(value: Decimal) -> String {
    format!("${:.2}", value)
}
