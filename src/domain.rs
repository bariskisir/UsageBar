use async_trait::async_trait;
use serde::Serialize;

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
#[derive(Debug, Clone, Default)]
pub struct ProviderResult {
    pub blocks: Vec<UsageBlock>,
    /// Ordered list of usage windows for the tray icon bar layout.
    /// Each window maps to one horizontal bar segment.
    pub windows: Vec<UsageBarWindow>,
    /// Optional plan/tier name, e.g. "Plus", "Pro", "Max". Shown next to the
    /// provider name in the tooltip.
    pub plan: Option<String>,
}

/// Maps an internal window label (`"5h"` / `"7d"`) to the user-facing name
/// shown in the tray tooltip and notifications (`"Session"` / `"Weekly"`).
/// Unknown labels pass through unchanged.
pub fn window_display_label(label: &str) -> &str {
    match label {
        "5h" => "Session",
        "7d" => "Weekly",
        other => other,
    }
}

/// A single horizontal bar segment in the tray icon.
#[derive(Debug, Clone)]
pub struct UsageBarWindow {
    /// Provider name, e.g. "Codex" or "Claude".
    pub provider_name: String,
    /// Time-window label, e.g. "5h" or "7d".
    pub window_label: String,
    /// How much of the limit is used (0–100).
    pub used_percent: f64,
}

/// A single line (or two lines) of usage information for the tooltip.
#[derive(Debug, Clone)]
pub struct UsageBlock {
    pub label: String,
    pub value: String,
    /// When `true`, label and value are shown on a single line separated by a space.
    pub inline: bool,
}

/// A provider card in the custom (non-legacy) tooltip.
///
/// Mirrors the CodexBar tray-panel `MenuCard`: a provider name header, then a
/// stack of metric rows (one per rate-limit window) and/or plain value lines
/// (balances / credits).
#[derive(Debug, Clone, Serialize)]
pub struct TooltipCard {
    /// Provider name shown as the card header, e.g. "Codex".
    pub title: String,
    /// Optional plan/tier shown next to the title, e.g. "Plus", "Max".
    pub plan: Option<String>,
    /// Progress-bar metric rows (rate-limit windows).
    pub metrics: Vec<TooltipMetric>,
    /// Plain value lines (e.g. a "$12.34" balance) with no progress bar.
    pub lines: Vec<String>,
}

/// A single metric row inside a [`TooltipCard`], mirroring CodexBar's
/// `MetricRow`: a window label, a usage bar, and a "N% used ·· reset" line.
#[derive(Debug, Clone, Serialize)]
pub struct TooltipMetric {
    /// Window label, e.g. "5h" or "7d".
    pub label: String,
    /// Used percentage, clamped to 0–100.
    pub percent: f64,
    /// Right-aligned reset hint, e.g. "2h 10m". Empty when unknown.
    pub detail: String,
}
