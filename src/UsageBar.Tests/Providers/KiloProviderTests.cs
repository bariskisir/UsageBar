using UsageBar.Domain;
using UsageBar.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class KiloProviderTests
{
    [Fact]
    public async Task Reports_credit_balance_when_no_pass_exists()
    {
        var json = """
        [
          { "result": { "data": { "json": {
            "creditBlocks": [
              { "amount_mUsd": 10000000, "balance_mUsd": 7500000 },
              { "amount_mUsd": 5000000, "balance_mUsd": 5000000 }
            ]
          } } } },
          { "result": { "data": { "json": { "subscription": null } } } },
          { "result": { "data": { "json": null } } }
        ]
        """;
        var handler = FakeHttpMessageHandler.Json(json);
        var provider = new KiloProvider(new HttpClient(handler));

        var result = await provider.GetUsageAsync(TestData.Context((CredentialNames.Kilo, "kilo-key")), CancellationToken.None);

        Assert.Equal("$12.50", Assert.IsType<BalanceResult>(result).BalanceText);
        var request = Assert.Single(handler.Requests);
        Assert.StartsWith(
            "https://app.kilo.ai/api/trpc/user.getCreditBlocks,kiloPass.getState,user.getAutoTopUpPaymentMethod?batch=1&input=",
            request.RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
        Assert.Contains("%220%22%3A%7B%22json%22%3Anull%7D", request.RequestUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("kilo-key", request.Headers.Authorization.Parameter);
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/json");
    }

    [Fact]
    public async Task Returns_null_without_api_key()
    {
        var provider = new KiloProvider(new HttpClient(FakeHttpMessageHandler.Json("{}")));
        var result = await provider.GetUsageAsync(TestData.Context(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Reports_pass_as_metric_when_subscription_exists()
    {
        var resetAt = TestData.FixedNow.AddHours(6);
        var json = $$"""
        [
          { "result": { "data": { "json": {
            "creditBlocks": [
              { "amount_mUsd": 20000000, "balance_mUsd": 12500000 }
            ]
          } } } },
          { "result": { "data": { "json": {
            "subscription": {
              "currentPeriodUsageUsd": 12.5,
              "currentPeriodBaseCreditsUsd": 20,
              "currentPeriodBonusCreditsUsd": 5,
              "tier": "tier_49",
              "nextBillingAt": "{{resetAt:O}}"
            }
          } } } },
          { "result": { "data": { "json": null } } }
        ]
        """;
        var provider = new KiloProvider(new HttpClient(FakeHttpMessageHandler.Json(json)));

        var result = await provider.GetUsageAsync(TestData.Context((CredentialNames.Kilo, "key")), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Equal("Kilo", metric.ProviderName);
        Assert.Equal("Pro - Balance $12.50", metric.Plan);
        var window = Assert.Single(metric.Windows);
        Assert.Equal("Pass", window.Label);
        Assert.Equal("kilo_pass", UsageBar.Application.IconLayout.WindowKey(metric.ProviderName, window.Label));
        Assert.Equal(50, window.UsedPercent);
        Assert.Equal("6h 0m", window.ResetText);
        Assert.Equal(15, provider.GetDisplayOrder(metric));
    }

    [Fact]
    public async Task Reports_pass_without_credit_balance_as_metric_only()
    {
        var json = """
        [
          { "result": { "data": { "json": { "creditBlocks": [] } } } },
          { "result": { "data": { "json": {
            "subscription": {
              "currentPeriodUsageUsd": 10,
              "currentPeriodBaseCreditsUsd": 20,
              "currentPeriodBonusCreditsUsd": 0,
              "tier": "tier_19"
            }
          } } } },
          { "result": { "data": { "json": null } } }
        ]
        """;
        var provider = new KiloProvider(new HttpClient(FakeHttpMessageHandler.Json(json)));

        var result = await provider.GetUsageAsync(TestData.Context((CredentialNames.Kilo, "key")), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Equal("Starter", metric.Plan);
        var window = Assert.Single(metric.Windows);
        Assert.Equal("Pass", window.Label);
        Assert.Equal(50, window.UsedPercent);
    }

    [Fact]
    public void Orders_metric_after_claude_and_balance_after_kimi()
    {
        var provider = new KiloProvider(new HttpClient(FakeHttpMessageHandler.Json("{}")));

        Assert.Equal(15, provider.GetDisplayOrder(new MetricResult("Kilo", null, [], [])));
        Assert.Equal(116, provider.GetDisplayOrder(new BalanceResult("Kilo", "$12.50")));
    }
}
