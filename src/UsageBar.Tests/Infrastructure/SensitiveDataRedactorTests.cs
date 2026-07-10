using System.Net.Http.Headers;
using UsageBar.Core.Infrastructure.Logging;
using Xunit;

namespace UsageBar.Tests;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void SafeUri_redacts_query_values_and_telegram_token()
    {
        var uri = new Uri("https://api.telegram.org/botsecret-token/sendMessage?chat_id=42&key=value");

        var safe = SensitiveDataRedactor.SafeUri(uri);

        Assert.DoesNotContain("secret-token", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("42", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("value", safe, StringComparison.Ordinal);
        Assert.Contains("bot<redacted>/sendMessage", safe, StringComparison.Ordinal);
        Assert.Contains("chat_id=<redacted>", safe, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeUri_redacts_discord_webhook_path()
    {
        var safe = SensitiveDataRedactor.SafeUri(
            new Uri("https://discord.com/api/webhooks/123/super-secret"));

        Assert.Equal("https://discord.com/<redacted-webhook>", safe);
    }

    [Fact]
    public void BodySnapshot_never_exposes_sensitive_scalar_values()
    {
        const string secret = "sentinel-secret-value";
        var json = $$"""
        {
          "access_token": "{{secret}}",
          "account_id": "{{secret}}",
          "nested": { "password": "{{secret}}", "status": "ok", "used_percent": 42.5 },
          "unknown_text": "{{secret}}"
        }
        """;

        var snapshot = SensitiveDataRedactor.BodySnapshot(json, "application/json");

        Assert.DoesNotContain(secret, snapshot, StringComparison.Ordinal);
        Assert.Contains("<redacted>", snapshot, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"ok\"", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("42.5", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void FormSnapshot_only_preserves_allowlisted_grant_type()
    {
        const string body = "grant_type=refresh_token&refresh_token=sentinel&client_secret=hidden";

        var snapshot = SensitiveDataRedactor.BodySnapshot(body, "application/x-www-form-urlencoded");

        Assert.Contains("grant_type=refresh_token", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_logging_returns_names_without_values()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "sentinel");
        request.Headers.Add("x-api-key", "sentinel-key");

        var headers = SensitiveDataRedactor.HeaderNames(request.Headers);

        Assert.Contains("Authorization", headers, StringComparison.Ordinal);
        Assert.Contains("x-api-key", headers, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel", headers, StringComparison.Ordinal);
    }
}
