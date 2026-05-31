use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tokio::sync::mpsc;

use crate::application::aggregator;
use crate::application::tooltip;
use crate::domain::{IUsageProvider, UsageBarWindow};
use crate::infrastructure::logger::AppLogger;
use crate::infrastructure::paths;
use crate::infrastructure::settings::SettingsService;
use crate::infrastructure::startup;
use crate::providers::claude::ClaudeProvider;
use crate::providers::codex::CodexProvider;
use crate::providers::deepgram::DeepgramProvider;
use crate::providers::deepseek::DeepSeekProvider;
use crate::providers::openrouter::OpenRouterProvider;
use crate::shell::tray::{TrayEvent, TrayIcon};

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
        let (tray, event_rx) = TrayIcon::new()?;
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
    previous_windows: Mutex<Vec<UsageBarWindow>>,
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
            previous_windows: Mutex::new(Vec::new()),
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

        self.tray.update_tooltip(&tooltip::format(&snapshot.blocks));
        self.tray.update_icon(&snapshot.windows)?;

        let messages = self.record_windows(&snapshot.windows);
        if !messages.is_empty() {
            self.tray
                .show_notification("Limit refreshed", &messages.join("\n"));
        }

        Ok(())
    }

    fn record_windows(&self, current_windows: &[UsageBarWindow]) -> Vec<String> {
        let mut messages = Vec::new();
        let Ok(mut previous) = self.previous_windows.lock() else {
            return messages;
        };

        for current in current_windows {
            let prev = find_previous_window(&previous, &current.provider_name, &current.window_label);
            if usage_decreased(prev.map(|w| w.used_percent), Some(current.used_percent)) {
                messages.push(format!(
                    "{} {} limit refreshed",
                    current.provider_name, current.window_label
                ));
            }
        }

        *previous = current_windows.to_vec();

        messages
    }
}

fn find_previous_window<'a>(
    windows: &'a [UsageBarWindow],
    provider: &str,
    label: &str,
) -> Option<&'a UsageBarWindow> {
    windows
        .iter()
        .find(|w| w.provider_name == provider && w.window_label == label)
}

fn usage_decreased(previous: Option<f64>, current: Option<f64>) -> bool {
    const MINIMUM_DECREASE: f64 = 0.01;
    match (previous, current) {
        (Some(prev), Some(curr)) => curr < prev - MINIMUM_DECREASE,
        _ => false,
    }
}
