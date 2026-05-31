use std::sync::Arc;
use std::time::Duration;
use tokio_util::sync::CancellationToken;

use crate::domain::{IUsageProvider, ProviderCredentials, ProviderResult, UsageBlock};

/// The result of a full refresh across all providers.
#[derive(Debug, Clone)]
pub struct UsageSnapshot {
    pub blocks: Vec<UsageBlock>,
    pub codex_primary_used_percent: Option<f64>,
    pub codex_secondary_used_percent: Option<f64>,
}

/// Runs all configured providers in parallel with a 45-second aggregate timeout.
pub async fn refresh_async(
    providers: &[Arc<dyn IUsageProvider>],
    credentials: &ProviderCredentials,
) -> UsageSnapshot {
    let cancellation_source = CancellationToken::new();
    let timeout_handle = tokio::spawn({
        let ct = cancellation_source.clone();
        async move {
            tokio::time::sleep(Duration::from_secs(45)).await;
            ct.cancel();
        }
    });

    let refresh_tasks = providers.iter().map(|provider| {
        let creds = credentials.clone();
        let ct = cancellation_source.clone();
        let provider = Arc::clone(provider);
        async move { refresh_provider_async(provider.as_ref(), &creds, ct).await }
    });

    let results: Vec<Option<ProviderResult>> = futures::future::join_all(refresh_tasks).await;

    timeout_handle.abort();

    let mut blocks = Vec::new();
    let mut codex_primary_used_percent: Option<f64> = None;
    let mut codex_secondary_used_percent: Option<f64> = None;

    for result in results {
        if let Some(r) = result {
            if codex_primary_used_percent.is_none() {
                codex_primary_used_percent = r.codex_primary_used_percent;
            }
            if codex_secondary_used_percent.is_none() {
                codex_secondary_used_percent = r.codex_secondary_used_percent;
            }
            blocks.extend(r.blocks);
        }
    }

    UsageSnapshot {
        blocks,
        codex_primary_used_percent,
        codex_secondary_used_percent,
    }
}

async fn refresh_provider_async(
    provider: &dyn IUsageProvider,
    credentials: &ProviderCredentials,
    cancellation_token: CancellationToken,
) -> Option<ProviderResult> {
    match provider.get_usage(credentials, cancellation_token).await {
        Ok(result) => result,
        Err(_err) => {
            // Individual provider errors are logged at the call site
            // (RefreshCoordinator::refresh_once).
            None
        }
    }
}
