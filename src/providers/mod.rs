use std::{sync::Arc, time::Duration};

use async_trait::async_trait;
use reqwest::Client;

use crate::domain::ProviderCategory;

pub mod claude;
pub mod codex;
pub mod context;
pub mod deepgram;
pub mod deepseek;
pub mod error;
pub mod formatting;
pub mod json;
pub mod openrouter;

pub use claude::{ClaudeAuth, ClaudeAuthReader, ClaudeProvider};
pub use codex::{CodexAuth, CodexAuthReader, CodexProvider};
pub use context::{DEEPGRAM_API_KEY, DEEPSEEK_API_KEY, OPENROUTER_API_KEY, ProviderQueryContext};
pub use deepgram::DeepgramProvider;
pub use deepseek::DeepSeekProvider;
pub use error::{ProviderError, ProviderResultValue};
pub use formatting::{currency, reset_duration};
pub use openrouter::OpenRouterProvider;

#[async_trait]
pub trait UsageProvider: Send + Sync {
    fn name(&self) -> &'static str;
    fn category(&self) -> ProviderCategory;
    async fn get_usage(&self, context: &ProviderQueryContext) -> ProviderResultValue;
}

pub fn providers(client: Client) -> Vec<Arc<dyn UsageProvider>> {
    vec![
        Arc::new(CodexProvider::new(
            client.clone(),
            CodexAuthReader::default(),
        )),
        Arc::new(ClaudeProvider::new(
            client.clone(),
            ClaudeAuthReader::default(),
        )),
        Arc::new(DeepSeekProvider::new(client.clone())),
        Arc::new(OpenRouterProvider::new(client.clone())),
        Arc::new(DeepgramProvider::new(client)),
    ]
}

pub fn http_client() -> Result<Client, reqwest::Error> {
    Client::builder()
        .timeout(Duration::from_secs(20))
        .user_agent("UsageBar/0.1")
        .build()
}
