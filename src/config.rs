use std::{
    fs,
    path::{Path, PathBuf},
};

use anyhow::{Context, Result};
use directories::{BaseDirs, ProjectDirs};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppSettings {
    pub refresh_period_minute: u64,
    pub high_percentage: f64,
    pub critical_percentage: f64,
    #[serde(rename = "DEEPSEEK_API_KEY", default)]
    pub deepseek_api_key: String,
    #[serde(rename = "OPENROUTER_API_KEY", default)]
    pub openrouter_api_key: String,
    #[serde(rename = "DEEPGRAM_API_KEY", default)]
    pub deepgram_api_key: String,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            refresh_period_minute: 5,
            high_percentage: 70.0,
            critical_percentage: 90.0,
            deepseek_api_key: String::new(),
            openrouter_api_key: String::new(),
            deepgram_api_key: String::new(),
        }
    }
}

impl AppSettings {
    pub fn normalize(mut self) -> Self {
        let defaults = Self::default();
        if self.refresh_period_minute == 0 {
            self.refresh_period_minute = defaults.refresh_period_minute;
        }
        if !(1.0..=100.0).contains(&self.high_percentage) {
            self.high_percentage = defaults.high_percentage;
        }
        if !(1.0..=100.0).contains(&self.critical_percentage) {
            self.critical_percentage = defaults.critical_percentage;
        }
        self
    }
}

#[derive(Debug, Clone)]
pub struct AppPaths {
    pub app_data_dir: PathBuf,
    pub local_data_dir: PathBuf,
    pub settings_file: PathBuf,
    pub log_file: PathBuf,
}

impl AppPaths {
    pub fn resolve() -> Result<Self> {
        let project = ProjectDirs::from("", "", "UsageBar")
            .context("could not resolve UsageBar application directories")?;
        let app_data_dir = project.config_dir().to_path_buf();
        let local_data_dir = project.data_local_dir().to_path_buf();
        Ok(Self {
            settings_file: app_data_dir.join("settings.json"),
            log_file: app_data_dir.join("app.log"),
            app_data_dir,
            local_data_dir,
        })
    }

    pub fn user_profile() -> Option<PathBuf> {
        BaseDirs::new().map(|dirs| dirs.home_dir().to_path_buf())
    }
}

#[derive(Debug, Clone)]
pub struct JsonSettingsStore {
    path: PathBuf,
}

impl JsonSettingsStore {
    pub fn new(path: PathBuf) -> Result<Self> {
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent).with_context(|| {
                format!("failed to create settings directory {}", parent.display())
            })?;
        }
        let store = Self { path };
        store.ensure_file()?;
        Ok(store)
    }

    pub fn read(&self) -> Result<AppSettings> {
        self.ensure_file()?;
        let raw = fs::read_to_string(&self.path)
            .with_context(|| format!("failed to read {}", self.path.display()))?;
        let settings: AppSettings = serde_json::from_str(&raw)
            .with_context(|| format!("failed to parse {}", self.path.display()))?;
        let normalized = settings.normalize();
        self.write(&normalized)?;
        Ok(normalized)
    }

    pub fn read_or_default(&self) -> AppSettings {
        match self.read() {
            Ok(settings) => settings,
            Err(error) => {
                tracing::warn!(?error, "failed to read settings; using defaults");
                AppSettings::default()
            }
        }
    }

    pub fn write(&self, settings: &AppSettings) -> Result<()> {
        let json = serde_json::to_string_pretty(settings)?;
        fs::write(&self.path, json)
            .with_context(|| format!("failed to write {}", self.path.display()))
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    fn ensure_file(&self) -> Result<()> {
        if !self.path.exists() {
            self.write(&AppSettings::default())?;
        }
        Ok(())
    }
}
