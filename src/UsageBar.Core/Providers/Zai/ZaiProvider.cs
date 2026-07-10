using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Reports Zai (z.ai) usage across token and time limit windows.</summary>
public sealed class ZaiProvider(HttpClient httpClient) : IUsageProvider
{
    private const string QuotaEndpoint = "https://api.z.ai/api/monitor/usage/quota/limit";

    public ProviderDescriptor Descriptor { get; } = new(
        "Zai", 19, ProviderAuthenticationKind.ApiKey, CredentialNames.Zai, 16, "zai", ["zai_*"]);

    public bool IsConfigured(ProviderQueryContext context) =>
        !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.Zai));

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.Zai);
        if (apiKey is null)
        {
            return null;
        }

        using var document = await ProviderHttp.GetJsonWithBearerAsync(httpClient, QuotaEndpoint, apiKey, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var windows = new List<UsageWindow>();

        if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var entry in root.EnumerateArray())
            {
                var window = ReadLimitEntry(entry, Descriptor.Name, context.Now);
                if (window is not null)
                {
                    windows.Add(window);
                }
            }
        }
        else if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            // Try common wrapper shapes.
            var data = root;
            if (ProviderJson.TryGetProperty(root, "data", out var dataProp))
            {
                data = dataProp;
            }

            if (data.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var entry in data.EnumerateArray())
                {
                    var window = ReadLimitEntry(entry, Descriptor.Name, context.Now);
                    if (window is not null)
                    {
                        windows.Add(window);
                    }
                }
            }
        }

        if (windows.Count == 0)
        {
            throw new ProviderException("Zai response did not contain usable limit entries.");
        }

        return new MetricResult(Descriptor.Name, null, windows);
    }

    private static UsageWindow? ReadLimitEntry(System.Text.Json.JsonElement entry, string providerName, DateTimeOffset now)
    {
        var type = ProviderJson.GetString(entry, "type") ?? "Limit";
        var unit = ProviderJson.GetString(entry, "unit");

        var label = !string.IsNullOrWhiteSpace(unit)
            ? $"{type} ({unit})"
            : type;

        // Prefer explicit percentage, fall back to usage/number calculation.
        var percentage = ProviderJson.GetDouble(entry, "percentage") ??
            ProviderJson.GetDouble(entry, "remaining_percentage");
        double? usedPercent = null;
        if (percentage is not null)
        {
            usedPercent = Math.Clamp(100.0 - percentage.Value, 0, 100);
        }
        else
        {
            var usage = ProviderJson.GetDecimal(entry, "usage");
            var number = ProviderJson.GetDecimal(entry, "number");
            if (usage is not null && number is not null && number.Value > 0)
            {
                usedPercent = (double)(usage.Value / number.Value * 100);
            }
        }

        if (usedPercent is null)
        {
            return null;
        }

        var nextResetTime = ProviderJson.GetString(entry, "nextResetTime");
        string? resetText = null;
        if (nextResetTime is not null &&
            DateTimeOffset.TryParse(nextResetTime, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            resetText = UsageFormatting.ResetDuration(parsed - now);
        }

        return new UsageWindow(providerName, label, Math.Clamp(usedPercent.Value, 0, 100), resetText);
    }
}
