using System.Net.Http.Headers;
using System.Text;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Reports Codebuff usage/quota percentage and remaining balance.</summary>
public sealed class CodebuffProvider(HttpClient httpClient) : IUsageProvider
{
    private const string DefaultBaseUrl = "https://www.codebuff.com";

    public ProviderDescriptor Descriptor { get; } = new(
        "Codebuff", 27, ProviderAuthenticationKind.ApiKey, CredentialNames.Codebuff, 14, "codebuff",
        ["codebuff_quota"]);

    public bool IsConfigured(ProviderQueryContext context) =>
        !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.Codebuff));

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.Codebuff);
        if (apiKey is null)
        {
            return null;
        }

        var endpoint = $"{DefaultBaseUrl.TrimEnd('/')}/api/v1/usage";

        var body = """{"fingerprintId":"codexbar-usage"}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var usage = ProviderJson.GetDecimal(root, "usage");
        var quota = ProviderJson.GetDecimal(root, "quota");

        if (usage is null || quota is null || quota.Value <= 0)
        {
            throw new ProviderException("Codebuff response did not contain valid usage and quota.");
        }

        var usedPercent = (double)(usage.Value / quota.Value * 100);
        var remainingBalance = ProviderJson.GetDecimal(root, "remainingBalance");
        var resetDate = ProviderJson.GetString(root, "next_quota_reset");
        string? resetText = null;
        if (resetDate is not null &&
            DateTimeOffset.TryParse(resetDate, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            resetText = UsageFormatting.ResetDuration(parsed - context.Now);
        }

        var planParts = new List<string>();
        if (remainingBalance is not null)
        {
            planParts.Add(UsageFormatting.Currency(remainingBalance.Value));
        }

        var plan = planParts.Count > 0 ? string.Join(" - ", planParts) : null;
        var window = new UsageWindow(Descriptor.Name, "Quota", usedPercent, resetText);

        return new MetricResult(Descriptor.Name, plan, [window]);
    }
}
