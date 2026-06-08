use async_trait::async_trait;
use reqwest::Client;
use serde_json::Value;

use crate::{
    domain::{ProviderCategory, ProviderResult},
    providers::{
        UsageProvider,
        context::{DEEPGRAM_API_KEY, ProviderQueryContext},
        error::{ProviderError, ProviderResultValue, ensure_success, invalid},
        formatting::currency,
        json::{
            enumerate_array_or_property, enumerate_array_or_property_or_root, get_decimal,
            get_string, url_encode_path_segment,
        },
    },
};

#[derive(Debug, Clone)]
pub struct DeepgramProvider {
    client: Client,
    projects_endpoint: String,
    balances_base_endpoint: String,
}

impl DeepgramProvider {
    pub fn new(client: Client) -> Self {
        Self {
            client,
            projects_endpoint: "https://api.deepgram.com/v1/projects".to_string(),
            balances_base_endpoint: "https://api.deepgram.com/v1/projects".to_string(),
        }
    }

    pub fn with_endpoints(
        client: Client,
        projects_endpoint: impl Into<String>,
        balances_base_endpoint: impl Into<String>,
    ) -> Self {
        Self {
            client,
            projects_endpoint: projects_endpoint.into(),
            balances_base_endpoint: balances_base_endpoint.into(),
        }
    }

    async fn first_project_id(&self, api_key: &str) -> Result<String, ProviderError> {
        let response = self
            .client
            .get(&self.projects_endpoint)
            .header("Authorization", format!("Token {api_key}"))
            .header("accept", "application/json")
            .send()
            .await?;
        ensure_success("Deepgram", response.status())?;
        let root: Value = response.json().await?;
        for project in enumerate_array_or_property(&root, "projects") {
            if let Some(project_id) = get_string(project, &["project_id", "projectId", "id"])
                && !project_id.trim().is_empty()
            {
                return Ok(project_id);
            }
        }
        Err(invalid("Deepgram", "response did not contain a project_id"))
    }

    async fn balance(&self, api_key: &str, project_id: &str) -> Result<f64, ProviderError> {
        let url = format!(
            "{}/{}/balances",
            self.balances_base_endpoint.trim_end_matches('/'),
            url_encode_path_segment(project_id)
        );
        let response = self
            .client
            .get(url)
            .header("Authorization", format!("Token {api_key}"))
            .header("accept", "application/json")
            .send()
            .await?;
        ensure_success("Deepgram", response.status())?;
        let root: Value = response.json().await?;
        let mut total = 0.0;
        let mut found = false;
        for balance in enumerate_array_or_property_or_root(&root, "balances") {
            let amount = get_decimal(balance, "amount")
                .or_else(|| get_decimal(balance, "balance"))
                .or_else(|| get_decimal(balance, "total_balance"));
            let Some(amount) = amount else {
                continue;
            };
            let units = get_string(balance, &["units", "currency"]);
            if units
                .as_deref()
                .is_some_and(|value| !value.eq_ignore_ascii_case("usd"))
            {
                continue;
            }
            total += amount;
            found = true;
        }
        if !found {
            return Err(invalid(
                "Deepgram",
                "response did not contain a balance amount",
            ));
        }
        Ok(total)
    }
}

#[async_trait]
impl UsageProvider for DeepgramProvider {
    fn name(&self) -> &'static str {
        "Deepgram"
    }

    fn category(&self) -> ProviderCategory {
        ProviderCategory::Balance
    }

    async fn get_usage(&self, context: &ProviderQueryContext) -> ProviderResultValue {
        let Some(api_key) = context.api_key(DEEPGRAM_API_KEY) else {
            return Ok(None);
        };
        let project_id = self.first_project_id(api_key).await?;
        let balance = self.balance(api_key, &project_id).await?;
        Ok(Some(ProviderResult::balance(
            "Deepgram",
            currency(balance, "$"),
        )))
    }
}
