using System.Net.Http.Headers;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;
/// <summary>Reports Chutes quota usage across 4-hour rolling and monthly windows.</summary>
public sealed class ChutesProvider(HttpClient httpClient) : ISingleResultUsageProvider
{
    private const string SubscriptionUsageEndpoint = "https://api.chutes.ai/users/me/subscription_usage";
    public ProviderDescriptor Descriptor { get; } = new("Chutes", 17, ProviderAuthenticationKind.ApiKey, CredentialNames.Chutes, 18, "chutes", ["chutes_4hrolling", "chutes_monthly"]);

    public bool IsConfigured(ProviderQueryContext context) => !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.Chutes));
    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.Chutes);
        if (apiKey is null)
        {
            return null;
        }

        // Try subscription_usage first, fall back to quotas.
        using (var request = new HttpRequestMessage(HttpMethod.Get, SubscriptionUsageEndpoint))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using (var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false))
            {
                var root = document.RootElement;
                var windows = new List<UsageWindow>();
                // Parse subscription_usage: expect percent fields.
                var fourHourPercent = ProviderJson.GetDouble(root, "usedPercent") ?? ProviderJson.GetDouble(root, "fourHourUsedPercent") ?? ProviderJson.GetDouble(root, "rolling_4h_used");
                var monthlyPercent = ProviderJson.GetDouble(root, "monthlyUsedPercent") ?? ProviderJson.GetDouble(root, "monthly_used");
                if (fourHourPercent is not null)
                {
                    windows.Add(new UsageWindow(Descriptor.Name, "4h Rolling", Math.Clamp(fourHourPercent.Value, 0, 100)));
                }

                if (monthlyPercent is not null)
                {
                    windows.Add(new UsageWindow(Descriptor.Name, "Monthly", Math.Clamp(monthlyPercent.Value, 0, 100)));
                }

                if (windows.Count == 0)
                {
                    throw new ProviderException("Chutes response did not contain usable quota windows.");
                }

                return new MetricResult(Descriptor.Name, null, windows);
            }
        }
    }
}