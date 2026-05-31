use serde::{Deserialize, Serialize};
use std::path::Path;

use crate::domain::ProviderCredentials;

/// JSON-serialisable application settings stored in `%APPDATA%\UsageBarRust\settings.json`.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppSettings {
    pub refresh_period_minute: i32,
    #[serde(rename = "DEEPSEEK_API_KEY")]
    pub deepseek_api_key: Option<String>,
    #[serde(rename = "OPENROUTER_API_KEY")]
    pub openrouter_api_key: Option<String>,
    #[serde(rename = "DEEPGRAM_API_KEY")]
    pub deepgram_api_key: Option<String>,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            refresh_period_minute: 5,
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

    /// Read from disk. Returns an error if the file is missing or malformed.
    pub async fn try_read(&self) -> anyhow::Result<AppSettings> {
        let json = tokio::fs::read_to_string(&self.settings_file_path).await?;
        let settings: AppSettings = serde_json::from_str(&json)?;
        let normalized = settings.normalize();
        self.write(&normalized).await?;
        Ok(normalized)
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
