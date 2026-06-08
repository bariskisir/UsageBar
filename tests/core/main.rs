use std::sync::Arc;

use async_trait::async_trait;
use chrono::{TimeDelta, TimeZone, Utc};
use usagebar::{
    app::{IconLayout, ThresholdNotifier, TooltipCardBuilder, UsageAggregator},
    config::AppSettings,
    domain::{NotificationLevel, ProviderCategory, ProviderPlan, ProviderResult, UsageWindow},
    providers::{
        ProviderQueryContext, ProviderResultValue, UsageProvider, currency, reset_duration,
    },
};

fn fixed_now() -> chrono::DateTime<Utc> {
    Utc.with_ymd_and_hms(2030, 1, 1, 0, 0, 0).unwrap()
}

fn window(provider: &str, label: &str, percent: f64) -> UsageWindow {
    UsageWindow::new(provider, label, percent, None)
}

#[test]
fn settings_normalize_repairs_invalid_numbers() {
    let settings = AppSettings {
        refresh_period_minute: 0,
        high_percentage: 0.0,
        critical_percentage: 150.0,
        ..AppSettings::default()
    }
    .normalize();

    assert_eq!(settings.refresh_period_minute, 5);
    assert_eq!(settings.high_percentage, 70.0);
    assert_eq!(settings.critical_percentage, 90.0);
}

#[test]
fn formatting_matches_legacy_display() {
    assert_eq!(reset_duration(TimeDelta::zero()), "now");
    assert_eq!(reset_duration(TimeDelta::minutes(5)), "5m");
    assert_eq!(reset_duration(TimeDelta::minutes(65)), "1h 5m");
    assert_eq!(reset_duration(TimeDelta::minutes(60 * 25)), "1d 1h");
    assert_eq!(currency(12.5, "$"), "$12.50");
    assert_eq!(currency(100.0, "\u{00A5}"), "\u{00A5}100.00");
}

#[tokio::test]
async fn aggregator_merges_results_and_isolates_failures() {
    let providers: Vec<Arc<dyn UsageProvider>> = vec![
        Arc::new(StubProvider::result(ProviderResult::metric(
            "Codex",
            Some("Pro".to_string()),
            vec![window("Codex", "Session", 10.0)],
        ))),
        Arc::new(StubProvider::none()),
        Arc::new(StubProvider::fail()),
        Arc::new(StubProvider::result(ProviderResult::balance(
            "DeepSeek", "$5.00",
        ))),
    ];

    let snapshot = UsageAggregator::refresh(
        &providers,
        &ProviderQueryContext::with_keys(fixed_now(), []),
    )
    .await;

    assert_eq!(snapshot.results.len(), 2);
    assert_eq!(snapshot.windows.len(), 1);
    assert_eq!(snapshot.plans[0].provider_name, "Codex");
    assert_eq!(snapshot.plans[0].plan, "Pro");
}

#[test]
fn tooltip_cards_order_metric_before_balance() {
    let snapshot = usagebar::domain::UsageSnapshot {
        results: vec![
            ProviderResult::balance("OpenRouter", "$1.00"),
            ProviderResult::metric(
                "Claude",
                Some("Max".to_string()),
                vec![window("Claude", "Session", 50.0)],
            ),
            ProviderResult::metric(
                "Codex",
                Some("Pro".to_string()),
                vec![window("Codex", "Session", 10.0)],
            ),
        ],
        windows: vec![],
        plans: vec![],
    };

    let cards = TooltipCardBuilder::build(&snapshot);
    let titles: Vec<_> = cards.iter().map(|card| card.title.as_str()).collect();
    assert_eq!(titles, vec!["Codex", "Claude", "OpenRouter"]);
}

#[test]
fn threshold_notifier_fires_once_and_resets() {
    let mut notifier = ThresholdNotifier::default();
    let settings = AppSettings::default();

    assert!(
        notifier
            .evaluate(&[window("Codex", "Session", 60.0)], &settings)
            .is_empty()
    );
    let high = notifier.evaluate(&[window("Codex", "Session", 75.0)], &settings);
    assert_eq!(high[0].level, NotificationLevel::High);
    assert!(
        notifier
            .evaluate(&[window("Codex", "Session", 80.0)], &settings)
            .is_empty()
    );

    let reset = notifier.evaluate(&[window("Codex", "Session", 50.0)], &settings);
    assert_eq!(reset[0].level, NotificationLevel::Reset);
    let high_again = notifier.evaluate(&[window("Codex", "Session", 75.0)], &settings);
    assert_eq!(high_again[0].level, NotificationLevel::High);
}

#[test]
fn threshold_notifier_emits_limit_reached_when_usage_reaches_100() {
    let mut notifier = ThresholdNotifier::default();
    let settings = AppSettings::default();

    assert!(
        notifier
            .evaluate(&[window("Codex", "Session", 99.0)], &settings)
            .is_empty()
    );

    let reached = notifier.evaluate(&[window("Codex", "Session", 100.0)], &settings);
    assert_eq!(reached.len(), 1);
    assert_eq!(reached[0].level, NotificationLevel::Critical);
    assert!(reached[0].message.contains("limit reached"));

    assert!(
        notifier
            .evaluate(&[window("Codex", "Session", 100.0)], &settings)
            .is_empty()
    );
}

#[test]
fn icon_layout_handles_codex_free_and_claude_pair() {
    let windows = vec![
        window("Codex", "Weekly", 25.0),
        window("Claude", "Session", 30.0),
        window("Claude", "Weekly", 40.0),
    ];
    let plans = vec![ProviderPlan {
        provider_name: "Codex".to_string(),
        plan: "Free".to_string(),
    }];

    let bars = IconLayout::compute(&windows, &plans);
    let providers: Vec<_> = bars.iter().map(|bar| bar.provider.as_str()).collect();
    assert_eq!(providers, vec!["Codex", "Claude", "Claude"]);
}

#[derive(Clone)]
enum StubMode {
    Result(ProviderResult),
    None,
    Fail,
}

struct StubProvider {
    mode: StubMode,
}

impl StubProvider {
    fn result(result: ProviderResult) -> Self {
        Self {
            mode: StubMode::Result(result),
        }
    }

    fn none() -> Self {
        Self {
            mode: StubMode::None,
        }
    }

    fn fail() -> Self {
        Self {
            mode: StubMode::Fail,
        }
    }
}

#[async_trait]
impl UsageProvider for StubProvider {
    fn name(&self) -> &'static str {
        "Stub"
    }

    fn category(&self) -> ProviderCategory {
        ProviderCategory::Balance
    }

    async fn get_usage(&self, _context: &ProviderQueryContext) -> ProviderResultValue {
        match &self.mode {
            StubMode::Result(result) => Ok(Some(result.clone())),
            StubMode::None => Ok(None),
            StubMode::Fail => Err(usagebar::providers::ProviderError::InvalidResponse {
                provider: "Stub",
                message: "boom".to_string(),
            }),
        }
    }
}
