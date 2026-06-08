use async_trait::async_trait;
use reqwest::Client;
use serde_json::Value;

use crate::{
    domain::{ProviderCategory, ProviderResult},
    providers::{
        UsageProvider,
        context::{DEEPSEEK_API_KEY, ProviderQueryContext},
        error::{ProviderResultValue, ensure_success, invalid},
        formatting::currency,
        json::{get_decimal, get_string},
    },
};

#[derive(Debug, Clone)]
pub struct DeepSeekProvider {
    client: Client,
    endpoint: String,
}

impl DeepSeekProvider {
    pub fn new(client: Client) -> Self {
        Self {
            client,
            endpoint: "https://api.deepseek.com/user/balance".to_string(),
        }
    }

    pub fn with_endpoint(client: Client, endpoint: impl Into<String>) -> Self {
        Self {
            client,
            endpoint: endpoint.into(),
        }
    }
}

#[async_trait]
impl UsageProvider for DeepSeekProvider {
    fn name(&self) -> &'static str {
        "DeepSeek"
    }

    fn category(&self) -> ProviderCategory {
        ProviderCategory::Balance
    }

    async fn get_usage(&self, context: &ProviderQueryContext) -> ProviderResultValue {
        let Some(api_key) = context.api_key(DEEPSEEK_API_KEY) else {
            return Ok(None);
        };
        let response = self
            .client
            .get(&self.endpoint)
            .bearer_auth(api_key)
            .send()
            .await?;
        ensure_success("DeepSeek", response.status())?;
        let root: Value = response.json().await?;
        let balance_infos = root
            .get("balance_infos")
            .and_then(Value::as_array)
            .ok_or_else(|| invalid("DeepSeek", "response did not contain balance_infos"))?;

        let mut usd = None;
        let mut cny = None;
        for item in balance_infos {
            let Some(amount) = get_decimal(item, "total_balance") else {
                continue;
            };
            match get_string(item, &["currency"])
                .unwrap_or_default()
                .to_ascii_uppercase()
                .as_str()
            {
                "USD" => usd = Some(amount),
                "CNY" => cny = Some(amount),
                _ => {}
            }
        }

        let mut parts = Vec::new();
        if let Some(value) = usd {
            parts.push(currency(value, "$"));
        }
        if let Some(value) = cny.filter(|value| *value != 0.0) {
            parts.push(currency(value, "\u{00A5}"));
        }
        if parts.is_empty() {
            return Err(invalid(
                "DeepSeek",
                "response did not contain a USD or CNY balance",
            ));
        }
        Ok(Some(ProviderResult::balance("DeepSeek", parts.join(" / "))))
    }
}
