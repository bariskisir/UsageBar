use std::{collections::HashMap, sync::Arc, time::Duration};

use chrono::Utc;
use futures::future::join_all;
use tokio::{sync::Mutex, task::JoinHandle, time};

use crate::{
    config::{AppSettings, JsonSettingsStore},
    domain::{
        NotificationLevel, ProviderCategory, ProviderPlan, ProviderResult, ThresholdNotification,
        TooltipCard, TooltipMetric, UsageSnapshot, UsageWindow,
    },
    providers::{ProviderQueryContext, UsageProvider},
};

pub struct UsageAggregator;

impl UsageAggregator {
    pub async fn refresh(
        providers: &[Arc<dyn UsageProvider>],
        context: &ProviderQueryContext,
    ) -> UsageSnapshot {
        let results = join_all(providers.iter().map(|provider| async move {
            match provider.get_usage(context).await {
                Ok(result) => result,
                Err(error) => {
                    tracing::warn!(
                        provider = provider.name(),
                        ?error,
                        "provider refresh failed"
                    );
                    None
                }
            }
        }))
        .await;

        let mut succeeded = Vec::new();
        let mut windows = Vec::new();
        let mut plans = Vec::new();

        for result in results.into_iter().flatten() {
            windows.extend(result.windows.clone());
            if let Some(plan) = result.plan.clone() {
                plans.push(ProviderPlan {
                    provider_name: result.provider_name.clone(),
                    plan,
                });
            }
            succeeded.push(result);
        }

        UsageSnapshot {
            results: succeeded,
            windows,
            plans,
        }
    }
}

pub struct TooltipCardBuilder;

impl TooltipCardBuilder {
    pub fn build(snapshot: &UsageSnapshot) -> Vec<TooltipCard> {
        let mut metric_cards = Vec::new();
        let mut balance_cards = Vec::new();

        for result in &snapshot.results {
            match result.category {
                ProviderCategory::Metric if !result.windows.is_empty() => {
                    metric_cards.push(metric_card(result));
                }
                ProviderCategory::Balance => balance_cards.push(TooltipCard {
                    title: result.provider_name.clone(),
                    plan: None,
                    metrics: Vec::new(),
                    lines: vec![result.balance_text.clone().unwrap_or_default()],
                }),
                _ => {}
            }
        }

        metric_cards.sort_by_key(|card| provider_rank(&card.title));
        balance_cards.sort_by_key(|card| provider_rank(&card.title));
        metric_cards.extend(balance_cards);
        metric_cards
    }
}

fn metric_card(result: &ProviderResult) -> TooltipCard {
    TooltipCard {
        title: result.provider_name.clone(),
        plan: result.plan.clone(),
        metrics: result
            .windows
            .iter()
            .map(|window| TooltipMetric {
                label: window.label.clone(),
                percent: window.used_percent,
                detail: window.reset_text.clone().unwrap_or_default(),
            })
            .collect(),
        lines: Vec::new(),
    }
}

fn provider_rank(name: &str) -> u8 {
    match name {
        "Codex" => 0,
        "Claude" => 1,
        _ => 2,
    }
}

#[derive(Debug, Default)]
pub struct ThresholdNotifier {
    fired_level: HashMap<String, u8>,
    previous_windows: Vec<UsageWindow>,
}

impl ThresholdNotifier {
    pub fn evaluate(
        &mut self,
        current_windows: &[UsageWindow],
        settings: &AppSettings,
    ) -> Vec<ThresholdNotification> {
        const NONE: u8 = 0;
        const HIGH: u8 = 1;
        const CRITICAL: u8 = 2;
        const LIMIT: u8 = 3;

        let high = settings.high_percentage / 100.0;
        let critical = settings.critical_percentage / 100.0;
        let mut notifications = Vec::new();

        for current in current_windows {
            let Some(previous) = self.previous_windows.iter().find(|window| {
                window.provider_name == current.provider_name && window.label == current.label
            }) else {
                continue;
            };

            let key = format!("{}|{}", current.provider_name, current.label);
            let fired = self.fired_level.get(&key).copied().unwrap_or(NONE);
            let current_fraction = current.used_percent / 100.0;
            let previous_fraction = previous.used_percent / 100.0;

            if current_fraction < previous_fraction {
                notifications.push(ThresholdNotification {
                    level: NotificationLevel::Reset,
                    message: format!(
                        "{} {} reset to {}%",
                        current.provider_name,
                        current.label,
                        display_percent(current_fraction)
                    ),
                });
                self.fired_level.remove(&key);
                continue;
            }

            if fired < LIMIT && previous_fraction < 1.0 && current_fraction >= 1.0 {
                notifications.push(ThresholdNotification {
                    level: NotificationLevel::Critical,
                    message: format!(
                        "{} {} at 100% - limit reached!",
                        current.provider_name, current.label
                    ),
                });
                self.fired_level.insert(key, LIMIT);
            } else if fired < CRITICAL
                && previous_fraction < critical
                && current_fraction >= critical
            {
                notifications.push(ThresholdNotification {
                    level: NotificationLevel::Critical,
                    message: format!(
                        "{} {} at {}% - critically high!",
                        current.provider_name,
                        current.label,
                        display_percent(current_fraction)
                    ),
                });
                self.fired_level.insert(key, CRITICAL);
            } else if fired < HIGH && previous_fraction < high && current_fraction >= high {
                notifications.push(ThresholdNotification {
                    level: NotificationLevel::High,
                    message: format!(
                        "{} {} at {}% - approaching limit",
                        current.provider_name,
                        current.label,
                        display_percent(current_fraction)
                    ),
                });
                self.fired_level.insert(key, HIGH);
            }
        }

        self.previous_windows = current_windows.to_vec();
        notifications
    }
}

fn display_percent(fraction: f64) -> i64 {
    (fraction * 100.0).round() as i64
}

#[derive(Debug, Clone, PartialEq)]
pub struct IconBar {
    pub used_percent: Option<f64>,
    pub provider: String,
}

pub struct IconLayout;

impl IconLayout {
    pub fn compute(windows: &[UsageWindow], plans: &[ProviderPlan]) -> Vec<IconBar> {
        let codex: Vec<_> = windows
            .iter()
            .filter(|w| w.provider_name == "Codex")
            .collect();
        let claude: Vec<_> = windows
            .iter()
            .filter(|w| w.provider_name == "Claude")
            .collect();

        let codex_session = find_window(&codex, "Session");
        let codex_weekly = find_window(&codex, "Weekly");
        let claude_session = find_window(&claude, "Session");
        let claude_weekly = find_window(&claude, "Weekly");

        let has_codex = !codex.is_empty();
        let has_claude = !claude.is_empty();
        let codex_plan_is_free = plans.iter().any(|plan| {
            plan.provider_name.eq_ignore_ascii_case("Codex")
                && plan.plan.eq_ignore_ascii_case("Free")
        });
        let codex_free_window = if codex_plan_is_free {
            codex_weekly.or(codex_session)
        } else {
            codex_weekly
        };
        let codex_is_free =
            codex_free_window.is_some() && (codex_plan_is_free || codex_session.is_none());
        let codex_is_pro = codex_session.is_some() && !codex_plan_is_free;
        let claude_is_subscriber = claude_session.is_some() && claude_weekly.is_some();

        let mut ordered = Vec::new();
        if codex_is_pro && has_claude && claude_is_subscriber {
            add(&mut ordered, codex_session);
            add(&mut ordered, codex_weekly);
            add(&mut ordered, claude_session);
            add(&mut ordered, claude_weekly);
        } else if codex_is_free && has_claude && claude_is_subscriber {
            add(&mut ordered, codex_free_window);
            add(&mut ordered, claude_session);
            add(&mut ordered, claude_weekly);
        } else if !has_codex && claude_is_subscriber {
            add(&mut ordered, claude_session);
            add(&mut ordered, claude_weekly);
        } else if codex_is_pro && !has_claude {
            add(&mut ordered, codex_session);
            add(&mut ordered, codex_weekly);
        } else if codex_is_free && !has_claude {
            add(&mut ordered, codex_free_window);
        } else if has_codex && has_claude {
            add(&mut ordered, codex_session);
            add(&mut ordered, codex_weekly);
            add(&mut ordered, claude_session);
            add(&mut ordered, claude_weekly);
        } else if has_claude {
            add(&mut ordered, claude_session);
            add(&mut ordered, claude_weekly);
        }

        if ordered.is_empty() {
            ordered.push(IconBar {
                used_percent: None,
                provider: "None".to_string(),
            });
        }
        ordered
    }
}

fn find_window<'a>(windows: &[&'a UsageWindow], label: &str) -> Option<&'a UsageWindow> {
    windows.iter().copied().find(|window| window.label == label)
}

fn add(bars: &mut Vec<IconBar>, window: Option<&UsageWindow>) {
    if let Some(window) = window {
        bars.push(IconBar {
            used_percent: Some(window.used_percent),
            provider: window.provider_name.clone(),
        });
    }
}

pub trait UsageView: Send + Sync {
    fn show_icon(&self, windows: &[UsageWindow], plans: &[ProviderPlan]);
    fn show_cards(&self, cards: &[TooltipCard]);
    fn notify(&self, level: NotificationLevel, message: &str);
}

pub struct UsageRefreshService<V: UsageView + 'static> {
    providers: Vec<Arc<dyn UsageProvider>>,
    settings: JsonSettingsStore,
    view: Arc<V>,
    thresholds: Arc<Mutex<ThresholdNotifier>>,
}

impl<V: UsageView + 'static> UsageRefreshService<V> {
    pub fn new(
        providers: Vec<Arc<dyn UsageProvider>>,
        settings: JsonSettingsStore,
        view: Arc<V>,
    ) -> Self {
        Self {
            providers,
            settings,
            view,
            thresholds: Arc::new(Mutex::new(ThresholdNotifier::default())),
        }
    }

    pub fn start(self: Arc<Self>) -> JoinHandle<()> {
        tokio::spawn(async move {
            loop {
                self.refresh_once().await;
                let settings = self.settings.read_or_default();
                time::sleep(Duration::from_secs(settings.refresh_period_minute * 60)).await;
            }
        })
    }

    pub async fn refresh_once(&self) {
        let settings = self.settings.read_or_default();
        let context = ProviderQueryContext::from_settings(&settings, Utc::now());
        let snapshot = UsageAggregator::refresh(&self.providers, &context).await;
        self.view.show_icon(&snapshot.windows, &snapshot.plans);
        self.view.show_cards(&TooltipCardBuilder::build(&snapshot));

        let mut thresholds = self.thresholds.lock().await;
        let notifications = thresholds.evaluate(&snapshot.windows, &settings);
        drop(thresholds);
        for level in [
            NotificationLevel::Critical,
            NotificationLevel::High,
            NotificationLevel::Reset,
        ] {
            let lines: Vec<_> = notifications
                .iter()
                .filter(|notification| notification.level == level)
                .map(|notification| notification.message.as_str())
                .collect();
            if !lines.is_empty() {
                self.view.notify(level, &lines.join("\n"));
            }
        }
    }
}
