use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tokio::sync::mpsc;

use crate::application::aggregator;
use crate::domain::{window_display_label, IUsageProvider, UsageBarWindow};
use crate::infrastructure::logger::AppLogger;
use crate::infrastructure::paths;
use crate::infrastructure::settings::SettingsService;
use crate::infrastructure::startup;
use crate::providers::claude::ClaudeProvider;
use crate::providers::codex::CodexProvider;
use crate::providers::deepgram::DeepgramProvider;
use crate::providers::deepseek::DeepSeekProvider;
use crate::providers::openrouter::OpenRouterProvider;
use crate::shell::tray::{NotificationLevel, TrayEvent, TrayIcon};

/// Top-level orchestrator — equivalent to `UsageBarRustHost` in the C# codebase.
pub struct UsageBarRustHost {
    logger: Arc<AppLogger>,
    settings: Arc<SettingsService>,
    #[allow(dead_code)]
    http_client: reqwest::Client,
    providers: Vec<Arc<dyn IUsageProvider>>,
}

impl UsageBarRustHost {
    /// Creates the host.  Does NOT start the refresh cycle or message loop.
    pub fn create_default() -> anyhow::Result<Self> {
        let logger = Arc::new(AppLogger::new(&paths::log_file_path()));
        startup::ensure_registered(&logger);

        let settings = Arc::new(SettingsService::new(&paths::settings_file_path()));

        let http_client = reqwest::Client::builder()
            .timeout(Duration::from_secs(20))
            .build()?;

        let providers: Vec<Arc<dyn IUsageProvider>> = vec![
            Arc::new(CodexProvider::new(http_client.clone())),
            Arc::new(ClaudeProvider::new(http_client.clone())),
            Arc::new(DeepSeekProvider::new(http_client.clone())),
            Arc::new(OpenRouterProvider::new(http_client.clone())),
            Arc::new(DeepgramProvider::new(http_client.clone())),
        ];

        Ok(Self {
            logger,
            settings,
            http_client,
            providers,
        })
    }

    /// Starts the refresh cycle and enters the Win32 message loop.
    /// Blocks until the user selects "Exit" from the context menu.
    pub fn run(self) -> anyhow::Result<()> {
        // Resolve the tooltip mode synchronously before the tray is created,
        // since it decides how the tray icon and its hover behaviour are set up.
        let use_legacy_tooltip = self.settings.read_sync().use_legacy_tooltip;

        let (tray, event_rx) = TrayIcon::new(use_legacy_tooltip, Arc::clone(&self.settings))?;
        let tray = Arc::new(tray);

        // Spawn the async refresh coordinator onto the Tokio runtime.
        let coordinator = RefreshCoordinator::new(
            Arc::clone(&self.settings),
            Arc::clone(&self.logger),
            self.providers.clone(),
            Arc::clone(&tray),
        );

        let coord_handle = tokio::spawn(async move {
            coordinator.run(event_rx).await;
        });

        // Block the main thread on the Win32 message loop.
        tray.run_message_loop();

        // The message loop has exited (user clicked Exit).
        coord_handle.abort();
        Ok(())
    }
}

// ---------------------------------------------------------------------------
// Refresh coordinator — runs on Tokio worker threads
// ---------------------------------------------------------------------------

struct RefreshCoordinator {
    settings: Arc<SettingsService>,
    logger: Arc<AppLogger>,
    providers: Vec<Arc<dyn IUsageProvider>>,
    tray: Arc<TrayIcon>,
    stopped: AtomicBool,
    /// Previous windows (provider, label, percent) for threshold-crossing detection.
    prev_windows: Mutex<Vec<UsageBarWindow>>,
    /// Per-window highest threshold notified this cycle.
    /// 0 = none, 1 = high warned, 2 = critical warned.
    /// Key: `"provider|label"`.  Reset to 0 when usage drops below `high_percentage`.
    notified_level: Mutex<HashMap<String, u8>>,
}

impl RefreshCoordinator {
    fn new(
        settings: Arc<SettingsService>,
        logger: Arc<AppLogger>,
        providers: Vec<Arc<dyn IUsageProvider>>,
        tray: Arc<TrayIcon>,
    ) -> Self {
        Self {
            settings,
            logger,
            providers,
            tray,
            stopped: AtomicBool::new(false),
            prev_windows: Mutex::new(Vec::new()),
            notified_level: Mutex::new(HashMap::new()),
        }
    }

    async fn run(&self, mut event_rx: mpsc::UnboundedReceiver<TrayEvent>) {
        // Initial refresh.
        self.refresh_once().await;

        loop {
            let period = {
                let s = self.settings.read().await;
                Duration::from_secs((s.refresh_period_minute.max(1) as u64) * 60)
            };

            tokio::select! {
                _ = tokio::time::sleep(period) => {
                    self.refresh_once().await;
                }
                event = event_rx.recv() => {
                    match event {
                        Some(TrayEvent::Refresh) => {
                            self.refresh_once().await;
                        }
                        Some(TrayEvent::Exit) | None => {
                            self.stopped.store(true, Ordering::SeqCst);
                            // PostQuitMessage is called from the window proc
                            // (main thread) when Exit is clicked.
                            return;
                        }
                    }
                }
            }
        }
    }

    async fn refresh_once(&self) {
        if self.stopped.load(Ordering::SeqCst) {
            return;
        }

        match self.try_refresh().await {
            Ok(()) => {}
            Err(err) => {
                self.logger
                    .log("Unexpected refresh failure.", Some(&err))
                    .await;
            }
        }
    }

    async fn try_refresh(&self) -> anyhow::Result<()> {
        let settings = self.settings.read().await;
        let credentials = settings.to_provider_credentials();

        let snapshot = aggregator::refresh_async(&self.providers, &credentials).await;

        self.tray
            .update_tooltip(&snapshot.blocks, &snapshot.windows, &snapshot.plans);
        self.tray.update_icon(&snapshot.windows, &snapshot.plans)?;

        let notifications = self.check_threshold_crossings(&snapshot.windows);
        // Group by severity so each level gets its own balloon glyph, emitting
        // the most severe first.
        for level in [
            NotificationLevel::Critical,
            NotificationLevel::High,
            NotificationLevel::Reset,
        ] {
            let lines: Vec<&str> = notifications
                .iter()
                .filter(|(l, _)| *l == level)
                .map(|(_, m)| m.as_str())
                .collect();
            if !lines.is_empty() {
                self.tray.show_notification(level, &lines.join("\n"));
            }
        }

        Ok(())
    }

    /// Checks every window against its previous value.  When usage climbs
    /// above `high_percentage` or `critical_percentage` (and was below it
    /// before), a matching notification line is produced.  Once notified,
    /// the same threshold will not fire again until usage drops back below
    /// the threshold (which resets the tracking flag).
    fn check_threshold_crossings(
        &self,
        windows: &[UsageBarWindow],
    ) -> Vec<(NotificationLevel, String)> {
        let mut messages = Vec::new();
        let settings = self.settings.read_sync();
        let high = settings.high_percentage;
        let critical = settings.critical_percentage;

        if high <= 0.0 && critical <= 0.0 {
            return messages;
        }

        let Ok(mut prev) = self.prev_windows.lock() else {
            return messages;
        };
        let Ok(mut noted) = self.notified_level.lock() else {
            return messages;
        };

        messages =
            check_threshold_crossings_for_windows(windows, high, critical, &mut prev, &mut noted);

        *prev = windows.to_vec();
        messages
    }
}

fn check_threshold_crossings_for_windows(
    windows: &[UsageBarWindow],
    high: f64,
    critical: f64,
    prev: &mut [UsageBarWindow],
    noted: &mut HashMap<String, u8>,
) -> Vec<(NotificationLevel, String)> {
    let mut messages = Vec::new();

    for current in windows {
        let key = format!("{}|{}", current.provider_name, current.window_label);

        // Find the matching previous window; `None` means this is the first
        // time we see this window (e.g. fresh start or new provider).
        let prev_pct = prev
            .iter()
            .find(|w| {
                w.provider_name == current.provider_name && w.window_label == current.window_label
            })
            .map(|w| w.used_percent);

        let curr_pct = current.used_percent;

        // On first sight of a window we only record the value; no notification
        // can fire without a real prior data point.
        let Some(prev_pct) = prev_pct else {
            continue;
        };

        let level = noted.get(&key).copied().unwrap_or(0);

        let label = window_display_label(&current.window_label);
        let pct = curr_pct.round() as i64;
        let mut next_level = level;
        if curr_pct < prev_pct {
            messages.push((
                NotificationLevel::Reset,
                format!("{} {} reset to {}%", current.provider_name, label, pct),
            ));
            next_level = 0;
        } else if next_level < 2 && prev_pct < critical && curr_pct >= critical {
            messages.push((
                NotificationLevel::Critical,
                format!(
                    "{} {} at {}% — critically high!",
                    current.provider_name, label, pct
                ),
            ));
            next_level = 2;
        } else if next_level < 1 && prev_pct < high && curr_pct >= high {
            messages.push((
                NotificationLevel::High,
                format!(
                    "{} {} at {}% — approaching limit",
                    current.provider_name, label, pct
                ),
            ));
            next_level = 1;
        }

        if next_level == 0 {
            noted.remove(&key);
        } else {
            noted.insert(key, next_level);
        }
    }

    messages
}

#[cfg(test)]
mod tests {
    use super::*;

    fn window(provider: &str, label: &str, percent: f64) -> UsageBarWindow {
        UsageBarWindow {
            provider_name: provider.to_string(),
            window_label: label.to_string(),
            used_percent: percent,
        }
    }

    #[test]
    fn threshold_reset_on_decrease_emits_message_and_allows_future_warning() {
        let mut prev = vec![window("Codex", "5h", 95.0)];
        let mut noted = HashMap::from([("Codex|5h".to_string(), 2)]);

        let messages = check_threshold_crossings_for_windows(
            &[window("Codex", "5h", 88.0)],
            70.0,
            90.0,
            &mut prev,
            &mut noted,
        );

        assert_eq!(
            messages,
            vec![(
                NotificationLevel::Reset,
                "Codex Session reset to 88%".to_string()
            )]
        );
        assert!(!noted.contains_key("Codex|5h"));

        prev = vec![window("Codex", "5h", 4.0)];
        let messages = check_threshold_crossings_for_windows(
            &[window("Codex", "5h", 72.0)],
            70.0,
            90.0,
            &mut prev,
            &mut noted,
        );

        assert_eq!(
            messages,
            vec![(
                NotificationLevel::High,
                "Codex Session at 72% — approaching limit".to_string()
            )]
        );
        assert_eq!(noted.get("Codex|5h"), Some(&1));
    }

    #[test]
    fn decrease_without_prior_limit_notification_still_emits_reset() {
        let mut prev = vec![window("Codex", "5h", 50.0)];
        let mut noted = HashMap::new();

        let messages = check_threshold_crossings_for_windows(
            &[window("Codex", "5h", 40.0)],
            70.0,
            90.0,
            &mut prev,
            &mut noted,
        );

        assert_eq!(
            messages,
            vec![(
                NotificationLevel::Reset,
                "Codex Session reset to 40%".to_string()
            )]
        );
        assert!(noted.is_empty());
    }

    #[test]
    fn missing_previous_window_does_not_emit_notification() {
        let mut prev = Vec::new();
        let mut noted = HashMap::new();

        let messages = check_threshold_crossings_for_windows(
            &[window("Codex", "5h", 95.0)],
            70.0,
            90.0,
            &mut prev,
            &mut noted,
        );

        assert!(messages.is_empty());
        assert!(noted.is_empty());
    }
}
