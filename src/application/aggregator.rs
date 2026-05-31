use std::sync::Arc;
use std::time::Duration;
use tokio_util::sync::CancellationToken;

use crate::domain::{IUsageProvider, ProviderCredentials, ProviderResult, UsageBarWindow, UsageBlock};

/// The result of a full refresh across all providers.
#[derive(Debug, Clone)]
pub struct UsageSnapshot {
    pub blocks: Vec<UsageBlock>,
    pub windows: Vec<UsageBarWindow>,
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
    let mut windows = Vec::new();

    for result in results {
        if let Some(r) = result {
            blocks.extend(r.blocks);
            windows.extend(r.windows);
        }
    }

    UsageSnapshot { blocks, windows }
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
