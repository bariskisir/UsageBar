using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Reports Synthetic quota usage across rolling-5h, weekly, and search-hourly windows.</summary>
public sealed class SyntheticProvider(HttpClient httpClient) : IUsageProvider
{
    private const string QuotasEndpoint = "https://api.synthetic.new/v2/quotas";

    public ProviderDescriptor Descriptor { get; } = new("Synthetic", DisplayOrder: 15);

    public void RefreshEnabled(ProviderQueryContext context) =>
        Descriptor.IsEnabled = !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.Synthetic));

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.Synthetic);
        if (apiKey is null)
        {
            return null;
        }

        using var document = await ProviderHttp.GetJsonWithBearerAsync(httpClient, QuotasEndpoint, apiKey, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var windows = new List<UsageWindow>();

        if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var quota in root.EnumerateArray())
            {
                var window = ReadQuotaWindow(quota, Descriptor.Name, context.Now);
                if (window is not null)
                {
                    windows.Add(window);
                }
            }
        }
        else if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var window = ReadQuotaWindow(property.Value, Descriptor.Name, context.Now);
                    if (window is not null)
                    {
                        windows.Add(window);
                    }
                }
            }
        }

        if (windows.Count == 0)
        {
            throw new ProviderException("Synthetic response did not contain usable quota windows.");
        }

        return new MetricResult(Descriptor.Name, null, windows, MetricWindows.EqualWeightBars(windows.ToArray()));
    }

    private static UsageWindow? ReadQuotaWindow(System.Text.Json.JsonElement quota, string providerName, DateTimeOffset now)
    {
        var usedPercent = ProviderJson.GetDouble(quota, "usedPercent");
        if (usedPercent is null)
        {
            return null;
        }

        var label = ProviderJson.GetString(quota, "label") ?? "Quota";
        var windowMinutes = ProviderJson.GetDouble(quota, "windowMinutes");
        var resetsAt = ProviderJson.GetString(quota, "resetsAt");

        string? resetText = null;
        if (resetsAt is not null &&
            DateTimeOffset.TryParse(resetsAt, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            resetText = UsageFormatting.ResetDuration(parsed - now);
        }
        else if (windowMinutes is not null && windowMinutes.Value > 0)
        {
            // Approximate: time window hint without exact reset timestamp.
            resetText = $"{windowMinutes.Value:0}m window";
        }

        return new UsageWindow(providerName, label, Math.Clamp(usedPercent.Value, 0, 100), resetText);
    }
}
