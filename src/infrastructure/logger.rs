use chrono::Local;
use std::io::Write;

/// Async file logger that writes timestamped messages to `app.log`.
pub struct AppLogger {
    log_file_path: std::path::PathBuf,
    gate: tokio::sync::Semaphore,
}

impl AppLogger {
    pub fn new(log_file_path: &std::path::Path) -> Self {
        if let Some(parent) = log_file_path.parent() {
            let _ = std::fs::create_dir_all(parent);
        }
        Self {
            log_file_path: log_file_path.to_path_buf(),
            gate: tokio::sync::Semaphore::new(1),
        }
    }

    pub async fn log(&self, message: &str, exception: Option<&anyhow::Error>) {
        let _permit = self.gate.acquire().await;
        let now = Local::now().format("%Y-%m-%d %H:%M:%S %z");
        let mut text = format!("{} | {}", now, message);

        if let Some(err) = exception {
            text.push_str(&format!("\n{:?}", err));
        }
        text.push('\n');

        // Best-effort append — failures are silently tolerated.
        if let Ok(mut file) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&self.log_file_path)
        {
            let _ = file.write_all(text.as_bytes());
        }
    }
}

/// Synchronous crash-logger for startup failures (before the async
/// infrastructure is ready).
pub struct FatalErrorLogger;

impl FatalErrorLogger {
    /// Write a fatal startup failure entry to `app.log`.
    /// Swallows all errors — the app has no UI surface for logging failures.
    pub fn log(error: &anyhow::Error) {
        let app_data = super::paths::app_data_directory();
        let _ = std::fs::create_dir_all(&app_data);

        let now = Local::now().format("%Y-%m-%d %H:%M:%S %z");
        let message = format!("{} | Fatal startup failure.\n{:?}\n", now, error);

        if let Ok(mut file) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(super::paths::log_file_path())
        {
            let _ = file.write_all(message.as_bytes());
        }
    }
}
