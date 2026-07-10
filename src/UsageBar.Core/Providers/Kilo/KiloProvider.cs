using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Reports Kilo credits, and Kilo Pass usage when the account has an active pass.</summary>
public sealed class KiloProvider(HttpClient httpClient) : IUsageProvider, IResultDisplayOrderProvider
{
    private const string TrpcEndpoint = "https://app.kilo.ai/api/trpc";
    private static readonly string[] Procedures =
    [
        "user.getCreditBlocks",
        "kiloPass.getState",
        "user.getAutoTopUpPaymentMethod",
    ];

    public ProviderDescriptor Descriptor { get; } = new(
        "Kilo", 30, ProviderAuthenticationKind.ApiKey, CredentialNames.Kilo, 9, "kilo", ["kilo_pass"]);

    public int GetDisplayOrder(ProviderResult result) => result switch
    {
        MetricResult => 15,
        BalanceResult => 116,
        _ => Descriptor.DisplayOrder,
    };

    public bool IsConfigured(ProviderQueryContext context) =>
        !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.Kilo));

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.Kilo);
        if (apiKey is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUsageUri());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
        var snapshot = Parse(document.RootElement);

        if (snapshot.Pass is not null)
        {
            var pass = snapshot.Pass.Value;
            var usedPercent = pass.Total > 0 ? (double)(pass.Used / pass.Total * 100) : 100;
            var resetText = pass.ResetsAt is null ? null : UsageFormatting.ResetDuration(pass.ResetsAt.Value - context.Now);
            var window = new UsageWindow(Descriptor.Name, "Pass", usedPercent, resetText);

            return new MetricResult(
                Descriptor.Name,
                PlanLine(snapshot.PlanName, snapshot.Credits),
                [window]);
        }

        if (snapshot.Credits is null)
        {
            throw new ProviderException("Kilo response did not contain credit or pass usage.");
        }

        return new BalanceResult(Descriptor.Name, BalanceText(snapshot.Credits.Value), snapshot.Credits.Value.Remaining);
    }

    private static Uri BuildUsageUri()
    {
        var endpoint = $"{TrpcEndpoint}/{string.Join(",", Procedures)}";
        var input = "{" + string.Join(",", Procedures.Select((_, index) => $"\"{index}\":{{\"json\":null}}")) + "}";
        return new Uri($"{endpoint}?batch=1&input={Uri.EscapeDataString(input)}");
    }

    private static KiloSnapshot Parse(JsonElement root)
    {
        var creditPayload = PayloadAt(root, 0);
        var passPayload = PayloadAt(root, 1);

        return new KiloSnapshot(
            Credits: ParseCredits(creditPayload),
            Pass: ParsePass(passPayload),
            PlanName: ParsePlanName(passPayload));
    }

    private static JsonElement? PayloadAt(JsonElement root, int index)
    {
        JsonElement entry;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() <= index)
            {
                return null;
            }

            entry = root[index];
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(index.ToString(CultureInfo.InvariantCulture), out var indexed))
        {
            entry = indexed;
        }
        else if (index == 0 && root.ValueKind == JsonValueKind.Object)
        {
            entry = root;
        }
        else
        {
            return null;
        }

        if (ProviderJson.TryGetProperty(entry, "error", out var error))
        {
            throw new ProviderException($"Kilo tRPC procedure failed: {ErrorMessage(error)}");
        }

        if (!ProviderJson.TryGetProperty(entry, "result", out var result))
        {
            return null;
        }

        if (ProviderJson.TryGetProperty(result, "data", out var data))
        {
            if (ProviderJson.TryGetProperty(data, "json", out var json))
            {
                return json.ValueKind == JsonValueKind.Null ? null : json;
            }

            return data.ValueKind == JsonValueKind.Null ? null : data;
        }

        if (ProviderJson.TryGetProperty(result, "json", out var resultJson))
        {
            return resultJson.ValueKind == JsonValueKind.Null ? null : resultJson;
        }

        return null;
    }

    private static KiloCredits? ParseCredits(JsonElement? payload)
    {
        if (payload is null)
        {
            return null;
        }

        if (FindProperty(payload.Value, "creditBlocks") is { ValueKind: JsonValueKind.Array } creditBlocks)
        {
            var total = 0m;
            var remaining = 0m;
            var sawTotal = false;
            var sawRemaining = false;

            foreach (var block in creditBlocks.EnumerateArray())
            {
                var amount = ProviderJson.GetDecimal(block, "amount_mUsd");
                if (amount is not null)
                {
                    total += amount.Value / 1_000_000m;
                    sawTotal = true;
                }

                var balance = ProviderJson.GetDecimal(block, "balance_mUsd");
                if (balance is not null)
                {
                    remaining += balance.Value / 1_000_000m;
                    sawRemaining = true;
                }
            }

            if (sawTotal || sawRemaining)
            {
                return new KiloCredits(
                    Used: sawTotal && sawRemaining ? Math.Max(0, total - remaining) : 0,
                    Total: Math.Max(0, total),
                    Remaining: Math.Max(0, remaining));
            }
        }

        var totalBalance = FindDecimal(payload.Value, "totalBalance_mUsd");
        if (totalBalance is not null)
        {
            var balance = Math.Max(0, totalBalance.Value / 1_000_000m);
            return new KiloCredits(0, balance, balance);
        }

        var used = FindDecimal(payload.Value, "used", "usedCredits", "creditsUsed", "consumed", "spent");
        var totalGeneric = FindDecimal(payload.Value, "total", "totalCredits", "creditsTotal", "limit");
        var remainingGeneric = FindDecimal(payload.Value, "remaining", "remainingCredits", "creditsRemaining");

        if (used is null && totalGeneric is null && remainingGeneric is null)
        {
            return null;
        }

        var resolvedTotal = totalGeneric ?? Math.Max(0, used.GetValueOrDefault() + remainingGeneric.GetValueOrDefault());
        var resolvedRemaining = remainingGeneric ?? Math.Max(0, resolvedTotal - used.GetValueOrDefault());
        return new KiloCredits(
            Used: Math.Max(0, used ?? resolvedTotal - resolvedRemaining),
            Total: Math.Max(0, resolvedTotal),
            Remaining: Math.Max(0, resolvedRemaining));
    }

    private static KiloPass? ParsePass(JsonElement? payload)
    {
        if (payload is null)
        {
            return null;
        }

        var source = FindProperty(payload.Value, "subscription") is { ValueKind: JsonValueKind.Object } subscription
            ? subscription
            : payload.Value;

        var used = ProviderJson.GetDecimal(source, "currentPeriodUsageUsd");
        var baseCredits = ProviderJson.GetDecimal(source, "currentPeriodBaseCreditsUsd");
        var bonusCredits = ProviderJson.GetDecimal(source, "currentPeriodBonusCreditsUsd") ?? 0;

        if (used is null || baseCredits is null)
        {
            return null;
        }

        var total = Math.Max(0, baseCredits.Value) + Math.Max(0, bonusCredits);
        return new KiloPass(
            Used: Math.Max(0, used.Value),
            Total: total,
            Bonus: Math.Max(0, bonusCredits),
            ResetsAt: ParseDate(
                ProviderJson.GetString(source, "nextBillingAt") ??
                ProviderJson.GetString(source, "nextRenewalAt") ??
                ProviderJson.GetString(source, "renewsAt") ??
                ProviderJson.GetString(source, "renewAt")));
    }

    private static string? ParsePlanName(JsonElement? payload)
    {
        if (payload is null)
        {
            return null;
        }

        var subscriptionProperty = FindProperty(payload.Value, "subscription");
        var hasSubscription = subscriptionProperty is not null;
        if (subscriptionProperty is { ValueKind: JsonValueKind.Null })
        {
            return null;
        }

        var source = subscriptionProperty is { ValueKind: JsonValueKind.Object }
            ? subscriptionProperty.Value
            : payload.Value;

        var tier = ProviderJson.GetString(source, "tier", "planName", "tierName", "passName", "subscriptionName");
        if (string.IsNullOrWhiteSpace(tier))
        {
            var hasPassShape =
                ProviderJson.GetDecimal(source, "currentPeriodUsageUsd") is not null ||
                ProviderJson.GetDecimal(source, "currentPeriodBaseCreditsUsd") is not null ||
                ProviderJson.GetDecimal(source, "currentPeriodBonusCreditsUsd") is not null;

            return hasSubscription || hasPassShape ? "Kilo Pass" : null;
        }

        return tier.Trim() switch
        {
            "tier_19" => "Starter",
            "tier_49" => "Pro",
            "tier_199" => "Expert",
            var value => value,
        };
    }

    private static JsonElement? FindProperty(JsonElement element, string propertyName, int maxDepth = 2)
    {
        if (element.ValueKind != JsonValueKind.Object || maxDepth < 0)
        {
            return null;
        }

        if (element.TryGetProperty(propertyName, out var property))
        {
            return property;
        }

        foreach (var child in element.EnumerateObject())
        {
            if (child.Value.ValueKind == JsonValueKind.Object)
            {
                var found = FindProperty(child.Value, propertyName, maxDepth - 1);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static decimal? FindDecimal(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = FindProperty(element, propertyName);
            if (property is not null)
            {
                var value = ProviderJson.GetDecimal(property.Value);
                if (value is not null)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var epoch))
        {
            var seconds = Math.Abs(epoch) > 10_000_000_000 ? epoch / 1000 : epoch;
            return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(seconds));
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static string? PlanLine(string? planName, KiloCredits? credits)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(planName))
        {
            parts.Add(planName.Trim());
        }

        if (credits is not null)
        {
            parts.Add($"Balance {BalanceText(credits.Value)}");
        }

        return parts.Count == 0 ? null : string.Join(" - ", parts);
    }

    private static string BalanceText(KiloCredits credits) => UsageFormatting.Currency(credits.Remaining);

    private static string ErrorMessage(JsonElement error)
    {
        var message = ProviderJson.GetString(error, "message") ??
            (ProviderJson.TryGetProperty(error, "json", out var json) ? ProviderJson.GetString(json, "message") : null);

        return string.IsNullOrWhiteSpace(message) ? "unknown error" : message;
    }

    private readonly record struct KiloSnapshot(KiloCredits? Credits, KiloPass? Pass, string? PlanName);

    private readonly record struct KiloCredits(decimal Used, decimal Total, decimal Remaining);

    private readonly record struct KiloPass(decimal Used, decimal Total, decimal Bonus, DateTimeOffset? ResetsAt);
}
