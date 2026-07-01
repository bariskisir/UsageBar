using System.Net.Http.Headers;
using System.Text;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Reports Alibaba Coding Plan quota usage across per-5h, per-week, and per-month windows.</summary>
public sealed class AlibabaProvider(HttpClient httpClient) : IUsageProvider
{
    private const string Endpoint = "https://modelstudio.console.alibabacloud.com/data/api.json?action=zeldaEasy.broadscope-bailian.codingPlan.queryCodingPlanInstanceInfoV2&product=broadscope-bailian&api=queryCodingPlanInstanceInfoV2&currentRegionId=ap-southeast-1";

    public ProviderDescriptor Descriptor { get; } = new("Alibaba", DisplayOrder: 23);

    public void RefreshEnabled(ProviderQueryContext context) =>
        Descriptor.IsEnabled = !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.Alibaba));

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.Alibaba);
        if (apiKey is null)
        {
            return null;
        }

        var body = """{"queryCodingPlanInstanceInfoRequest":{"commodityCode":"sfm_codingplan_public_intl"}}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("X-DashScope-API-Key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        // Navigate common Alibaba response wrapping.
        var data = root;
        foreach (var key in new[] { "data", "result", "output" })
        {
            if (ProviderJson.TryGetProperty(data, key, out var next) &&
                next.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                data = next;
            }
        }

        var planName = ProviderJson.GetString(data, "planName", "plan_name", "codingPlanName");
        var windows = new List<UsageWindow>();

        // Look for quota windows in common response shapes.
        var quotaKeys = new[] { "quotas", "quotaList", "quota_info", "usageInfo" };
        System.Text.Json.JsonElement quotasElement = default;
        var foundQuotas = false;
        foreach (var key in quotaKeys)
        {
            if (ProviderJson.TryGetProperty(data, key, out var q) && q.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                quotasElement = q;
                foundQuotas = true;
                break;
            }
        }

        if (foundQuotas)
        {
            foreach (var quota in quotasElement.EnumerateArray())
            {
                var window = ReadQuotaWindow(quota, Descriptor.Name, context.Now);
                if (window is not null)
                {
                    windows.Add(window);
                }
            }
        }

        // Fallback: look for 5h/week/month percentage fields directly on data.
        if (windows.Count == 0)
        {
            TryAddWindow(data, "per5Hour", "5h", windows, context.Now);
            TryAddWindow(data, "perWeek", "Weekly", windows, context.Now);
            TryAddWindow(data, "perMonth", "Monthly", windows, context.Now);
        }

        if (windows.Count == 0)
        {
            throw new ProviderException("Alibaba response did not contain usable quota windows.");
        }

        return new MetricResult(Descriptor.Name, planName, windows, MetricWindows.EqualWeightBars(windows.ToArray()));
    }

    private static void TryAddWindow(
        System.Text.Json.JsonElement element,
        string key,
        string label,
        List<UsageWindow> windows,
        DateTimeOffset now)
    {
        var percent = ProviderJson.GetDouble(element, $"{key}UsedPercent") ??
            ProviderJson.GetDouble(element, $"usedPercent{key}") ??
            ProviderJson.GetDouble(element, key);

        if (percent is not null)
        {
            windows.Add(new UsageWindow("Alibaba", label, Math.Clamp(percent.Value, 0, 100)));
        }
    }

    private static UsageWindow? ReadQuotaWindow(System.Text.Json.JsonElement quota, string providerName, DateTimeOffset now)
    {
        var label = ProviderJson.GetString(quota, "label", "name", "type", "windowLabel") ?? "Quota";
        var usedPercent = ProviderJson.GetDouble(quota, "usedPercent")
            ?? ProviderJson.GetDouble(quota, "used_percent")
            ?? ProviderJson.GetDouble(quota, "usagePercent");

        if (usedPercent is null)
        {
            var used = ProviderJson.GetDecimal(quota, "used") ?? ProviderJson.GetDecimal(quota, "usage");
            var total = ProviderJson.GetDecimal(quota, "total") ?? ProviderJson.GetDecimal(quota, "limit") ?? ProviderJson.GetDecimal(quota, "quota");
            if (used is not null && total is not null && total.Value > 0)
            {
                usedPercent = (double)(used.Value / total.Value * 100);
            }
        }

        if (usedPercent is null)
        {
            return null;
        }

        var resetTime = ProviderJson.GetString(quota, "resetsAt", "resetTime", "nextResetTime");
        string? resetText = null;
        if (resetTime is not null &&
            DateTimeOffset.TryParse(resetTime, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            resetText = UsageFormatting.ResetDuration(parsed - now);
        }

        return new UsageWindow(providerName, label, Math.Clamp(usedPercent.Value, 0, 100), resetText);
    }
}
