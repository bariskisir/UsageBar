use reqwest::StatusCode;
use thiserror::Error;

use crate::domain::ProviderResult;

#[derive(Debug, Error)]
pub enum ProviderError {
    #[error("{provider} request failed with HTTP {status}")]
    HttpStatus {
        provider: &'static str,
        status: StatusCode,
    },
    #[error("{provider} response was not usable: {message}")]
    InvalidResponse {
        provider: &'static str,
        message: String,
    },
    #[error(transparent)]
    Http(#[from] reqwest::Error),
    #[error(transparent)]
    Json(#[from] serde_json::Error),
    #[error(transparent)]
    Io(#[from] std::io::Error),
}

pub type ProviderResultValue = Result<Option<ProviderResult>, ProviderError>;

pub(crate) fn ensure_success(
    provider: &'static str,
    status: StatusCode,
) -> Result<(), ProviderError> {
    if status.is_success() {
        Ok(())
    } else {
        Err(ProviderError::HttpStatus { provider, status })
    }
}

pub(crate) fn invalid(provider: &'static str, message: impl Into<String>) -> ProviderError {
    ProviderError::InvalidResponse {
        provider,
        message: message.into(),
    }
}
