use std::fs;

use chrono::{TimeZone, Utc};
use httpmock::{Method::GET, MockServer};
use tempfile::NamedTempFile;
use usagebar::providers::{
    ClaudeAuthReader, ClaudeProvider, CodexAuthReader, CodexProvider, DEEPGRAM_API_KEY,
    DEEPSEEK_API_KEY, DeepSeekProvider, DeepgramProvider, OPENROUTER_API_KEY, OpenRouterProvider,
    ProviderQueryContext, UsageProvider,
};

fn fixed_now() -> chrono::DateTime<Utc> {
    Utc.with_ymd_and_hms(2030, 1, 1, 0, 0, 0).unwrap()
}

fn client() -> reqwest::Client {
    reqwest::Client::builder().build().unwrap()
}

#[tokio::test]
async fn codex_parses_windows_and_plan() {
    let server = MockServer::start();
    let reset = fixed_now().timestamp() + 130 * 60;
    server.mock(|when, then| {
        when.method(GET).path("/usage");
        then.status(200).json_body_obj(&serde_json::json!({
            "plan_type": "pro",
            "rate_limit": {
                "primary_window": { "used_percent": 53.0, "reset_at": reset },
                "secondary_window": { "used_percent": 12.5, "reset_at": reset }
            }
        }));
    });

    let auth = NamedTempFile::new().unwrap();
    fs::write(
        auth.path(),
        r#"{ "tokens": { "access_token": "token", "account_id": "account" } }"#,
    )
    .unwrap();
    let provider = CodexProvider::with_endpoint(
        client(),
        CodexAuthReader::new(auth.path().to_path_buf()),
        server.url("/usage"),
    );

    let result = provider
        .get_usage(&ProviderQueryContext::with_keys(fixed_now(), []))
        .await
        .unwrap()
        .unwrap();

    assert_eq!(result.plan.as_deref(), Some("Pro"));
    assert_eq!(result.windows[0].label, "Session");
    assert_eq!(result.windows[0].reset_text.as_deref(), Some("2h 10m"));
}

#[tokio::test]
async fn claude_parses_windows_and_plan() {
    let server = MockServer::start();
    server.mock(|when, then| {
        when.method(GET).path("/usage");
        then.status(200).json_body_obj(&serde_json::json!({
            "five_hour": { "utilization": 88.0, "resets_at": "2030-01-01T02:10:00Z" },
            "seven_day": { "utilization": 40.0, "resets_at": "2030-01-08T00:00:00Z" }
        }));
    });

    let auth = NamedTempFile::new().unwrap();
    fs::write(
        auth.path(),
        r#"{ "claudeAiOauth": { "accessToken": "token", "subscriptionType": "max" } }"#,
    )
    .unwrap();
    let provider = ClaudeProvider::with_endpoint(
        client(),
        ClaudeAuthReader::new(auth.path().to_path_buf()),
        server.url("/usage"),
    );

    let result = provider
        .get_usage(&ProviderQueryContext::with_keys(fixed_now(), []))
        .await
        .unwrap()
        .unwrap();

    assert_eq!(result.plan.as_deref(), Some("Max"));
    assert_eq!(result.windows[0].used_percent, 88.0);
}

#[tokio::test]
async fn balance_providers_parse_expected_shapes() {
    let server = MockServer::start();
    server.mock(|when, then| {
        when.method(GET).path("/deepseek");
        then.status(200).json_body_obj(&serde_json::json!({
            "balance_infos": [
                { "currency": "USD", "total_balance": "12.34" },
                { "currency": "CNY", "total_balance": "100.00" }
            ]
        }));
    });
    server.mock(|when, then| {
        when.method(GET).path("/openrouter");
        then.status(200).json_body_obj(&serde_json::json!({
            "data": { "total_credits": 20.0, "total_usage": 7.5 }
        }));
    });

    let deepseek = DeepSeekProvider::with_endpoint(client(), server.url("/deepseek"));
    let openrouter = OpenRouterProvider::with_endpoint(client(), server.url("/openrouter"));

    let deepseek_result = deepseek
        .get_usage(&ProviderQueryContext::with_keys(
            fixed_now(),
            [(DEEPSEEK_API_KEY, "key")],
        ))
        .await
        .unwrap()
        .unwrap();
    let openrouter_result = openrouter
        .get_usage(&ProviderQueryContext::with_keys(
            fixed_now(),
            [(OPENROUTER_API_KEY, "key")],
        ))
        .await
        .unwrap()
        .unwrap();

    assert_eq!(
        deepseek_result.balance_text.as_deref(),
        Some("$12.34 / \u{00A5}100.00")
    );
    assert_eq!(openrouter_result.balance_text.as_deref(), Some("$12.50"));
}

#[tokio::test]
async fn deepgram_sums_usd_balances_for_first_project() {
    let server = MockServer::start();
    server.mock(|when, then| {
        when.method(GET).path("/projects");
        then.status(200)
            .json_body_obj(&serde_json::json!({ "projects": [ { "project_id": "p1" } ] }));
    });
    server.mock(|when, then| {
        when.method(GET).path("/projects/p1/balances");
        then.status(200).json_body_obj(&serde_json::json!({
            "balances": [
                { "amount": 5.0, "units": "usd" },
                { "amount": 2.5, "units": "usd" }
            ]
        }));
    });

    let provider = DeepgramProvider::with_endpoints(
        client(),
        server.url("/projects"),
        server.url("/projects"),
    );
    let result = provider
        .get_usage(&ProviderQueryContext::with_keys(
            fixed_now(),
            [(DEEPGRAM_API_KEY, "key")],
        ))
        .await
        .unwrap()
        .unwrap();

    assert_eq!(result.balance_text.as_deref(), Some("$7.50"));
}
