using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Reports Venice balance — USD when active, DIEM with epoch allocation percentage otherwise.</summary>
public sealed class VeniceProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new(
        "Venice", 108, ProviderAuthenticationKind.ApiKey, CredentialNames.Venice, SettingsOrder: 11);

    protected override string CredentialName => CredentialNames.Venice;

    protected override async Task<BalanceFetchResult> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var document = await ProviderHttp.GetJsonWithBearerAsync(httpClient, "https://api.venice.ai/api/v1/billing/balance", apiKey, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var canConsume = ProviderJson.TryGetProperty(root, "canConsume", out var cc) &&
            cc.ValueKind == System.Text.Json.JsonValueKind.True;
        var currency = ProviderJson.GetString(root, "consumptionCurrency")?.ToUpperInvariant();

        ProviderJson.TryGetProperty(root, "balances", out var balances);
        var usd = balances.ValueKind != System.Text.Json.JsonValueKind.Undefined
            ? ProviderJson.GetDecimal(balances, "usd")
            : ProviderJson.GetDecimal(root, "usd");
        var diem = balances.ValueKind != System.Text.Json.JsonValueKind.Undefined
            ? ProviderJson.GetDecimal(balances, "diem")
            : ProviderJson.GetDecimal(root, "diem");
        var allocation = ProviderJson.GetDecimal(root, "diemEpochAllocation");

        if (!canConsume)
        {
            return new BalanceFetchResult("Unavailable");
        }

        // Active USD currency: show dollar balance.
        if (currency == "USD" && usd is > 0)
        {
            return new BalanceFetchResult(UsageFormatting.Currency(usd.Value), usd.Value);
        }

        // Non-USD active currency with DIEM + allocation: show percentage.
        if (currency != "USD" && diem is not null && allocation is > 0)
        {
            var usedAmount = allocation.Value - diem.Value;
            var usedPercent = Math.Clamp((double)(usedAmount / allocation.Value * 100), 0, 100);
            var text = $"DIEM {diem.Value:0.00} / {allocation.Value:0.00} ({usedPercent:0.#}%)";
            return new BalanceFetchResult(text);
        }

        // DIEM active currency without allocation.
        if (currency == "DIEM" && diem is > 0)
        {
            return new BalanceFetchResult($"DIEM {diem.Value:0.00}");
        }

        // DIEM balance (no currency field).
        if (diem is > 0)
        {
            if (allocation is > 0)
            {
                var usedAmount = allocation.Value - diem.Value;
                var usedPercent = Math.Clamp((double)(usedAmount / allocation.Value * 100), 0, 100);
                return new BalanceFetchResult($"DIEM {diem.Value:0.00} / {allocation.Value:0.00} ({usedPercent:0.#}%)");
            }

            return new BalanceFetchResult($"DIEM {diem.Value:0.00}");
        }

        // USD balance fallback (even without canConsume currency).
        if (usd is > 0)
        {
            return new BalanceFetchResult(UsageFormatting.Currency(usd.Value), usd.Value);
        }

        throw new ProviderException("Venice response did not contain a usable balance.");
    }
}
