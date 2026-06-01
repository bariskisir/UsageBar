use serde::{Deserialize, Serialize};
use std::path::Path;

use crate::domain::ProviderCredentials;

/// JSON-serialisable application settings stored in `%APPDATA%\UsageBarRust\settings.json`.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppSettings {
    pub refresh_period_minute: i32,
    /// When `false` (the default), usage is shown in the custom CodexBar-style
    /// tooltip window which is not limited to 128 characters. When `true`, the
    /// legacy native Win32 tray tooltip (`NOTIFYICONDATA.szTip`) is used.
    #[serde(default)]
    pub use_legacy_tooltip: bool,
    /// Usage percentage threshold (0–100) at which a "high usage" notification
    /// is shown when the value crosses this threshold upward.
    #[serde(default = "default_high_percentage")]
    pub high_percentage: f64,
    /// Usage percentage threshold (0–100) at which a "critical usage"
    /// notification is shown when the value crosses this threshold upward.
    #[serde(default = "default_critical_percentage")]
    pub critical_percentage: f64,
    #[serde(rename = "DEEPSEEK_API_KEY")]
    pub deepseek_api_key: Option<String>,
    #[serde(rename = "OPENROUTER_API_KEY")]
    pub openrouter_api_key: Option<String>,
    #[serde(rename = "DEEPGRAM_API_KEY")]
    pub deepgram_api_key: Option<String>,
}

fn default_high_percentage() -> f64 {
    70.0
}
fn default_critical_percentage() -> f64 {
    90.0
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            refresh_period_minute: 5,
            use_legacy_tooltip: false,
            high_percentage: 70.0,
            critical_percentage: 90.0,
            deepseek_api_key: Some(String::new()),
            openrouter_api_key: Some(String::new()),
            deepgram_api_key: Some(String::new()),
        }
    }
}

impl AppSettings {
    /// Clamp the refresh period and replace null keys with empty strings.
    pub fn normalize(self) -> Self {
        Self {
            refresh_period_minute: if self.refresh_period_minute > 0 {
                self.refresh_period_minute
            } else {
                Self::default().refresh_period_minute
            },
            use_legacy_tooltip: self.use_legacy_tooltip,
            high_percentage: self.high_percentage.clamp(0.0, 100.0),
            critical_percentage: self.critical_percentage.clamp(0.0, 100.0),
            deepseek_api_key: Some(self.deepseek_api_key.unwrap_or_default()),
            openrouter_api_key: Some(self.openrouter_api_key.unwrap_or_default()),
            deepgram_api_key: Some(self.deepgram_api_key.unwrap_or_default()),
        }
    }

    /// Resolve each credential: settings value first, environment variable fallback.
    pub fn to_provider_credentials(&self) -> ProviderCredentials {
        ProviderCredentials {
            deepseek_api_key: Self::resolve_credential(
                self.deepseek_api_key.as_deref().unwrap_or(""),
                "DEEPSEEK_API_KEY",
            ),
            openrouter_api_key: Self::resolve_credential(
                self.openrouter_api_key.as_deref().unwrap_or(""),
                "OPENROUTER_API_KEY",
            ),
            deepgram_api_key: Self::resolve_credential(
                self.deepgram_api_key.as_deref().unwrap_or(""),
                "DEEPGRAM_API_KEY",
            ),
        }
    }

    fn resolve_credential(settings_value: &str, env_var_name: &str) -> String {
        if !settings_value.trim().is_empty() {
            return settings_value.to_string();
        }
        std::env::var(env_var_name).unwrap_or_default()
    }
}

/// Reads and writes the settings JSON file.
pub struct SettingsService {
    settings_file_path: String,
}

impl SettingsService {
    pub fn new(settings_file_path: &Path) -> Self {
        if let Some(parent) = settings_file_path.parent() {
            let _ = std::fs::create_dir_all(parent);
        }
        Self {
            settings_file_path: settings_file_path.to_string_lossy().into_owned(),
        }
    }

    /// Read (or create with defaults) and normalise the settings file.
    pub async fn read(&self) -> AppSettings {
        self.ensure_file_exists();

        match self.try_read().await {
            Ok(settings) => settings,
            Err(_) => {
                // If anything goes wrong, return defaults.
                // The caller (RefreshCoordinator) logs the error separately if needed.
                AppSettings::default()
            }
        }
    }

    /// Read and normalise the settings file synchronously.
    ///
    /// Used at startup (before the Tokio refresh loop spins up) to resolve
    /// options that decide how the tray window is created, such as
    /// [`AppSettings::use_legacy_tooltip`]. Falls back to defaults on any error.
    pub fn read_sync(&self) -> AppSettings {
        self.ensure_file_exists();
        std::fs::read_to_string(&self.settings_file_path)
            .ok()
            .and_then(|json| serde_json::from_str::<AppSettings>(&json).ok())
            .map(AppSettings::normalize)
            .unwrap_or_default()
    }

    /// Read from disk. Returns an error if the file is missing or malformed.
    pub async fn try_read(&self) -> anyhow::Result<AppSettings> {
        let json = tokio::fs::read_to_string(&self.settings_file_path).await?;
        let settings: AppSettings = serde_json::from_str(&json)?;
        let normalized = settings.normalize();
        self.write(&normalized).await?;
        Ok(normalized)
    }

    /// Write settings synchronously — used from the tray window proc, which
    /// runs on the main thread and cannot `.await`.
    pub fn write_sync(&self, settings: &AppSettings) -> anyhow::Result<()> {
        let json = serde_json::to_string_pretty(settings)?;
        std::fs::write(&self.settings_file_path, json)?;
        Ok(())
    }

    async fn write(&self, settings: &AppSettings) -> anyhow::Result<()> {
        let json = serde_json::to_string_pretty(settings)?;
        tokio::fs::write(&self.settings_file_path, json).await?;
        Ok(())
    }

    fn ensure_file_exists(&self) {
        if Path::new(&self.settings_file_path).exists() {
            return;
        }
        let json =
            serde_json::to_string_pretty(&AppSettings::default()).unwrap_or_else(|_| "{}".to_string());
        let _ = std::fs::write(&self.settings_file_path, json);
    }
}
