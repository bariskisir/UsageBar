use async_trait::async_trait;

/// Interface that every usage provider must implement.
#[async_trait]
pub trait IUsageProvider: Send + Sync {
    /// Human-readable provider name used in log messages.
    #[allow(dead_code)]
    fn name(&self) -> &str;

    /// Fetch the latest usage/balance data from the provider.
    /// Returns `None` when the provider is not configured (missing credentials).
    async fn get_usage(
        &self,
        credentials: &ProviderCredentials,
        cancellation_token: tokio_util::sync::CancellationToken,
    ) -> anyhow::Result<Option<ProviderResult>>;
}

/// API keys read from settings (or their environment-variable fallbacks).
#[derive(Debug, Clone, Default)]
pub struct ProviderCredentials {
    pub deepseek_api_key: String,
    pub openrouter_api_key: String,
    pub deepgram_api_key: String,
}

/// The result returned by a successful provider query.
#[derive(Debug, Clone)]
pub struct ProviderResult {
    pub blocks: Vec<UsageBlock>,
    /// Codex 5h (primary) window usage percentage — used for icon top half.
    pub codex_primary_used_percent: Option<f64>,
    /// Codex 7d (secondary) window usage percentage — used for icon bottom half.
    pub codex_secondary_used_percent: Option<f64>,
}

/// A single line (or two lines) of usage information for the tooltip.
#[derive(Debug, Clone)]
pub struct UsageBlock {
    pub label: String,
    pub value: String,
    /// When `true`, label and value are shown on a single line separated by a space.
    pub inline: bool,
}
