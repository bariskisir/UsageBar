using System.Text.Json;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

public sealed class CommandCodeProvider(HttpClient httpClient)
    : ISingleResultUsageProvider
{
    private const string SubscriptionEndpoint = "https://api.commandcode.ai/alpha/billing/subscriptions";
    private const string CreditsEndpoint = "https://api.commandcode.ai/alpha/billing/credits";

    public ProviderDescriptor Descriptor { get; } = new("Command Code", 6, ProviderAuthenticationKind.ApiKey,
        CredentialNames.CommandCode, SettingsOrder: 5, IconKey: "commandcode",
        IconLayoutKeys: ["command_code_session", "command_code_weekly"]);

    public bool IsConfigured(ProviderQueryContext context) =>
        !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.CommandCode));

    public async Task<ProviderResult?> GetUsageAsync(
        ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.CommandCode);
        if (apiKey is null)
        {
            return null;
        }

        var subTask = FetchSubscriptionAsync(apiKey, cancellationToken);
        var creditsTask = FetchCreditsDocumentAsync(apiKey, cancellationToken);
        await Task.WhenAll(subTask, creditsTask).ConfigureAwait(false);

        var planId = await subTask.ConfigureAwait(false);

        using (var creditsDoc = await creditsTask.ConfigureAwait(false))
        {
            var parsed = ParseCreditsResponse(creditsDoc.RootElement);

            UsageWindow? fiveHour = null;
            UsageWindow? weekly = null;

            if (TryGetWindow(parsed.WindowLimits, "fiveHour", out var used, out var cap, out var resetAt))
            {
                var resetText = resetAt is { } r ? UsageFormatting.ResetDuration(r - context.Now) : null;
                fiveHour = new UsageWindow(Descriptor.Name, "Session",
                    cap > 0 ? (used / cap) * 100.0 : 0,
                    resetText: resetText,
                    resetAt: resetAt);
            }

            if (TryGetWindow(parsed.WindowLimits, "weekly", out used, out cap, out resetAt))
            {
                var resetText = resetAt is { } r ? UsageFormatting.ResetDuration(r - context.Now) : null;
                weekly = new UsageWindow(Descriptor.Name, "Weekly",
                    cap > 0 ? (used / cap) * 100.0 : 0,
                    resetText: resetText,
                    resetAt: resetAt);
            }

            var windows = MetricWindows.Require(Descriptor.Name, fiveHour, weekly);
            return new MetricResult(Descriptor.Name, PlanLabel(planId), windows);
        }
    }

    private async Task<string?> FetchSubscriptionAsync(string apiKey, CancellationToken cancellationToken)
    {
        using (var document = await ProviderHttp.GetJsonWithBearerAsync(
                   httpClient, SubscriptionEndpoint, apiKey, cancellationToken).ConfigureAwait(false))
        {
            if (ProviderJson.TryGetProperty(document.RootElement, "data", out var data) &&
                data.ValueKind == JsonValueKind.Object)
            {
                return ProviderJson.GetString(data, "planId");
            }
        }

        return null;
    }

    private async Task<JsonDocument> FetchCreditsDocumentAsync(string apiKey, CancellationToken cancellationToken)
    {
        return await ProviderHttp.GetJsonWithBearerAsync(
            httpClient, CreditsEndpoint, apiKey, cancellationToken).ConfigureAwait(false);
    }

    private static CreditsResponse ParseCreditsResponse(JsonElement root)
    {
        var purchasedCredits = 0m;
        var freeCredits = 0m;
        var windowLimits = default(JsonElement);

        if (ProviderJson.TryGetProperty(root, "credits", out var credits) &&
            credits.ValueKind == JsonValueKind.Object)
        {
            purchasedCredits = ProviderJson.GetDecimal(credits, "purchasedCredits") ?? 0;
            freeCredits = ProviderJson.GetDecimal(credits, "freeCredits") ?? 0;
        }

        if (ProviderJson.TryGetProperty(root, "windowLimits", out var limits) &&
            limits.ValueKind == JsonValueKind.Object)
        {
            windowLimits = limits.Clone();
        }

        return new CreditsResponse(purchasedCredits, freeCredits, windowLimits);
    }

    private static bool TryGetWindow(JsonElement windowLimits, string key,
        out double used, out double cap, out DateTimeOffset? resetAt)
    {
        used = 0;
        cap = 0;
        resetAt = null;

        if (!ProviderJson.TryGetProperty(windowLimits, key, out var window) ||
            window.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        used = ProviderJson.GetDouble(window, "used") ?? 0;
        cap = ProviderJson.GetDouble(window, "cap") ?? 0;

        var resetAtMs = ProviderJson.GetDouble(window, "resetAt");
        if (resetAtMs is > 0)
        {
            resetAt = DateTimeOffset.UnixEpoch.AddMilliseconds(resetAtMs.Value);
        }

        return cap > 0;
    }

    private static string? PlanLabel(string? planId)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return null;
        }

        var normalized = planId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "individual-go" => "Go",
            "individual" => "Individual",
            "pro" => "Pro",
            "team" => "Team",
            "enterprise" => "Enterprise",
            "free" => "Free",
            _ => UsageFormatting.Capitalize(normalized),
        };
    }

    private readonly record struct CreditsResponse(
        decimal PurchasedCredits,
        decimal FreeCredits,
        JsonElement WindowLimits);
}
