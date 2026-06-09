using System.Net;
using UsageBar.Domain;
using UsageBar.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class BalanceProviderTests
{
    [Fact]
    public async Task DeepSeek_shows_both_usd_and_cny_when_cny_is_nonzero()
    {
        var json = """
        {
          "balance_infos": [
            { "currency": "CNY", "total_balance": "100.00" },
            { "currency": "USD", "total_balance": "12.34" }
          ]
        }
        """;
        var provider = new DeepSeekProvider(new HttpClient(FakeHttpMessageHandler.Json(json)));

        var result = await provider.GetUsageAsync(TestData.Context((CredentialNames.DeepSeek, "key")), CancellationToken.None);

        var balance = Assert.IsType<BalanceResult>(result);
        Assert.Equal("DeepSeek", balance.ProviderName);
        Assert.Equal("$12.34 / ¥100.00", balance.BalanceText);
    }

    [Fact]
    public async Task DeepSeek_shows_only_usd_when_cny_is_zero()
    {
        var json = """
        {
          "balance_infos": [
            { "currency": "USD", "total_balance": "12.34" },
            { "currency": "CNY", "total_balance": "0" }
          ]
        }
        """;
        var provider = new DeepSeekProvider(new HttpClient(FakeHttpMessageHandler.Json(json)));

        var result = await provider.GetUsageAsync(TestData.Context((CredentialNames.DeepSeek, "key")), CancellationToken.None);

        Assert.Equal("$12.34", Assert.IsType<BalanceResult>(result).BalanceText);
    }

    [Fact]
    public async Task DeepSeek_returns_null_without_api_key()
    {
        var provider = new DeepSeekProvider(new HttpClient(FakeHttpMessageHandler.Json("{}")));
        var result = await provider.GetUsageAsync(TestData.Context(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task OpenRouter_reports_credits_minus_usage()
    {
        var json = """{ "data": { "total_credits": 20, "total_usage": 7.5 } }""";
        var provider = new OpenRouterProvider(new HttpClient(FakeHttpMessageHandler.Json(json)));

        var result = await provider.GetUsageAsync(TestData.Context((CredentialNames.OpenRouter, "key")), CancellationToken.None);

        Assert.Equal("$12.50", Assert.IsType<BalanceResult>(result).BalanceText);
    }

    [Fact]
    public async Task Deepgram_sums_usd_balances_for_first_project()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            var json = url.Contains("/balances", StringComparison.Ordinal)
                ? """{ "balances": [ { "amount": 5.00, "units": "usd" }, { "amount": 2.50, "units": "usd" } ] }"""
                : """{ "projects": [ { "project_id": "p1" } ] }""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        });
        var provider = new DeepgramProvider(new HttpClient(handler));

        var result = await provider.GetUsageAsync(TestData.Context((CredentialNames.Deepgram, "key")), CancellationToken.None);

        Assert.Equal("$7.50", Assert.IsType<BalanceResult>(result).BalanceText);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Balance_provider_throws_on_http_error()
    {
        var provider = new OpenRouterProvider(new HttpClient(FakeHttpMessageHandler.Json("{}", HttpStatusCode.Unauthorized)));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetUsageAsync(TestData.Context((CredentialNames.OpenRouter, "key")), CancellationToken.None));
    }
}
