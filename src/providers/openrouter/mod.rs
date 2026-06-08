use async_trait::async_trait;
use reqwest::Client;
use serde_json::Value;

use crate::{
    domain::{ProviderCategory, ProviderResult},
    providers::{
        UsageProvider,
        context::{OPENROUTER_API_KEY, ProviderQueryContext},
        error::{ProviderResultValue, ensure_success, invalid},
        formatting::currency,
        json::get_decimal,
    },
};

#[derive(Debug, Clone)]
pub struct OpenRouterProvider {
    client: Client,
    endpoint: String,
}

impl OpenRouterProvider {
    pub fn new(client: Client) -> Self {
        Self {
            client,
            endpoint: "https://openrouter.ai/api/v1/credits".to_string(),
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
impl UsageProvider for OpenRouterProvider {
    fn name(&self) -> &'static str {
        "OpenRouter"
    }

    fn category(&self) -> ProviderCategory {
        ProviderCategory::Balance
    }

    async fn get_usage(&self, context: &ProviderQueryContext) -> ProviderResultValue {
        let Some(api_key) = context.api_key(OPENROUTER_API_KEY) else {
            return Ok(None);
        };
        let response = self
            .client
            .get(&self.endpoint)
            .bearer_auth(api_key)
            .send()
            .await?;
        ensure_success("OpenRouter", response.status())?;
        let root: Value = response.json().await?;
        let data = root
            .get("data")
            .ok_or_else(|| invalid("OpenRouter", "response did not contain data"))?;
        let total_credits = get_decimal(data, "total_credits")
            .ok_or_else(|| invalid("OpenRouter", "response did not contain total_credits"))?;
        let total_usage = get_decimal(data, "total_usage")
            .ok_or_else(|| invalid("OpenRouter", "response did not contain total_usage"))?;
        Ok(Some(ProviderResult::balance(
            "OpenRouter",
            currency(total_credits - total_usage, "$"),
        )))
    }
}
