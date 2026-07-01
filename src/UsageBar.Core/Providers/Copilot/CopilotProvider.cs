using System.Net.Http.Headers;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Reports GitHub Copilot usage: premium interactions and chat quota windows.</summary>
public sealed class CopilotProvider(HttpClient httpClient) : IUsageProvider
{
    private const string CopilotEndpoint = "https://api.github.com/copilot_internal/user";

    public ProviderDescriptor Descriptor { get; } = new("Copilot", DisplayOrder: 5);

    public void RefreshEnabled(ProviderQueryContext context) =>
        Descriptor.IsEnabled = !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.Copilot));

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.Copilot);
        if (apiKey is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, CopilotEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("token", apiKey);
        request.Headers.TryAddWithoutValidation("Editor-Version", "vscode/1.96.2");
        request.Headers.TryAddWithoutValidation("Editor-Plugin-Version", "copilot-chat/0.26.7");
        request.Headers.TryAddWithoutValidation("User-Agent", "GitHubCopilotChat/0.26.7");
        request.Headers.TryAddWithoutValidation("X-Github-Api-Version", "2025-04-01");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var plan = UsageFormatting.Capitalize(ProviderJson.GetString(root, "copilot_plan") ?? "unknown");
        var resetDate = ProviderJson.GetString(root, "quota_reset_date");
        DateTimeOffset? parsedReset = null;
        if (resetDate is not null &&
            DateTimeOffset.TryParse(resetDate, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var pr))
        {
            parsedReset = pr;
        }

        ProviderJson.TryGetProperty(root, "quota_snapshots", out var snapshots);

        var premium = ReadQuotaWindow(snapshots, "premium_interactions", "Premium", Descriptor.Name, context.Now, parsedReset);
        var chat = ReadQuotaWindow(snapshots, "chat", "Chat", Descriptor.Name, context.Now, parsedReset);

        var windows = new List<UsageWindow>();
        if (premium is not null) windows.Add(premium);
        if (chat is not null) windows.Add(chat);

        if (windows.Count == 0)
        {
            // Token-based billing may have zero-entitlement placeholder quotas — surface plan without usage.
            throw new ProviderException("Copilot response did not contain usable quota windows.");
        }

        return new MetricResult(Descriptor.Name, plan, windows, MetricWindows.EqualWeightBars(windows.ToArray()));
    }

    private static UsageWindow? ReadQuotaWindow(
        System.Text.Json.JsonElement snapshots,
        string snapshotKey,
        string label,
        string providerName,
        DateTimeOffset now,
        DateTimeOffset? resetDate)
    {
        if (!ProviderJson.TryGetProperty(snapshots, snapshotKey, out var quota))
        {
            return null;
        }

        var entitlement = ProviderJson.GetDecimal(quota, "entitlement");
        var remaining = ProviderJson.GetDecimal(quota, "remaining");
        var hasPercentRemaining = !ProviderJson.TryGetProperty(quota, "hasPercentRemaining", out var hpr) || hpr.ValueKind != System.Text.Json.JsonValueKind.False;

        // Skip placeholder quotas: zero-entitlement, zero-remaining without percent_remaining data.
        var isPlaceholder = entitlement is 0 && remaining is 0 && !hasPercentRemaining;
        if (isPlaceholder)
        {
            return null;
        }

        // Skip quotas with no usable percentage.
        if (!hasPercentRemaining)
        {
            return null;
        }

        var isUnlimited = ProviderJson.TryGetProperty(quota, "unlimited", out var ul) &&
            ul.ValueKind != System.Text.Json.JsonValueKind.False;

        double usedPercent;
        if (isUnlimited)
        {
            usedPercent = 0;
        }
        else
        {
            var percentRemaining = ProviderJson.GetDouble(quota, "percent_remaining");
            if (percentRemaining is null)
            {
                // Derive from entitlement/remaining.
                if (entitlement is > 0 && remaining is not null)
                {
                    percentRemaining = (double)(remaining.Value / entitlement.Value * 100);
                }
                else
                {
                    return null;
                }
            }

            usedPercent = Math.Clamp(100.0 - percentRemaining.Value, 0, 100);
        }

        var overQuota = usedPercent > 100 ? $"{usedPercent:0.}% used" : null;
        var resetText = overQuota ?? (resetDate.HasValue ? UsageFormatting.ResetDuration(resetDate.Value - now) : null);

        return new UsageWindow(providerName, label, Math.Clamp(usedPercent, 0, 100), resetText);
    }
}
