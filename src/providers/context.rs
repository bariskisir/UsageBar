use std::{collections::HashMap, env};

use chrono::{DateTime, Utc};

use crate::config::AppSettings;

pub const DEEPSEEK_API_KEY: &str = "DEEPSEEK_API_KEY";
pub const OPENROUTER_API_KEY: &str = "OPENROUTER_API_KEY";
pub const DEEPGRAM_API_KEY: &str = "DEEPGRAM_API_KEY";

#[derive(Debug, Clone)]
pub struct ProviderQueryContext {
    pub now: DateTime<Utc>,
    api_keys: HashMap<String, String>,
}

impl ProviderQueryContext {
    pub fn from_settings(settings: &AppSettings, now: DateTime<Utc>) -> Self {
        let mut api_keys = HashMap::new();
        api_keys.insert(
            DEEPSEEK_API_KEY.to_string(),
            resolve_key(&settings.deepseek_api_key, DEEPSEEK_API_KEY),
        );
        api_keys.insert(
            OPENROUTER_API_KEY.to_string(),
            resolve_key(&settings.openrouter_api_key, OPENROUTER_API_KEY),
        );
        api_keys.insert(
            DEEPGRAM_API_KEY.to_string(),
            resolve_key(&settings.deepgram_api_key, DEEPGRAM_API_KEY),
        );
        Self { now, api_keys }
    }

    pub fn with_keys(
        now: DateTime<Utc>,
        keys: impl IntoIterator<Item = (&'static str, &'static str)>,
    ) -> Self {
        Self {
            now,
            api_keys: keys
                .into_iter()
                .map(|(name, value)| (name.to_string(), value.to_string()))
                .collect(),
        }
    }

    pub fn api_key(&self, name: &str) -> Option<&str> {
        if let Some(value) = self
            .api_keys
            .get(name)
            .map(String::as_str)
            .filter(|value| !value.trim().is_empty())
        {
            return Some(value);
        }

        let value = env::var(name).ok()?;
        let value = Box::leak(value.into_boxed_str());
        (!value.trim().is_empty()).then_some(value)
    }
}

fn resolve_key(settings_value: &str, env_name: &str) -> String {
    if !settings_value.trim().is_empty() {
        return settings_value.to_string();
    }
    env::var(env_name).unwrap_or_default()
}
