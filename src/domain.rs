use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ProviderCategory {
    Metric,
    Balance,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct UsageWindow {
    pub provider_name: String,
    pub label: String,
    pub used_percent: f64,
    pub reset_text: Option<String>,
}

impl UsageWindow {
    pub fn new(
        provider_name: impl Into<String>,
        label: impl Into<String>,
        used_percent: f64,
        reset_text: Option<String>,
    ) -> Self {
        Self {
            provider_name: provider_name.into(),
            label: label.into(),
            used_percent: used_percent.clamp(0.0, 100.0),
            reset_text,
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct ProviderResult {
    pub provider_name: String,
    pub category: ProviderCategory,
    pub plan: Option<String>,
    pub windows: Vec<UsageWindow>,
    pub balance_text: Option<String>,
}

impl ProviderResult {
    pub fn metric(
        provider_name: impl Into<String>,
        plan: Option<String>,
        windows: Vec<UsageWindow>,
    ) -> Self {
        Self {
            provider_name: provider_name.into(),
            category: ProviderCategory::Metric,
            plan,
            windows,
            balance_text: None,
        }
    }

    pub fn balance(provider_name: impl Into<String>, balance_text: impl Into<String>) -> Self {
        Self {
            provider_name: provider_name.into(),
            category: ProviderCategory::Balance,
            plan: None,
            windows: Vec::new(),
            balance_text: Some(balance_text.into()),
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct ProviderPlan {
    pub provider_name: String,
    pub plan: String,
}

#[derive(Debug, Clone, PartialEq, Default)]
pub struct UsageSnapshot {
    pub results: Vec<ProviderResult>,
    pub windows: Vec<UsageWindow>,
    pub plans: Vec<ProviderPlan>,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
pub struct TooltipMetric {
    pub label: String,
    pub percent: f64,
    pub detail: String,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
pub struct TooltipCard {
    pub title: String,
    pub plan: Option<String>,
    pub metrics: Vec<TooltipMetric>,
    pub lines: Vec<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NotificationLevel {
    Reset,
    High,
    Critical,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ThresholdNotification {
    pub level: NotificationLevel,
    pub message: String,
}
