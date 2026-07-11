using System.Net.Http.Headers;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;
/// <summary>Reports ElevenLabs character-credit usage as a percentage-based quota window.</summary>
public sealed class ElevenLabsProvider(HttpClient httpClient) : ISingleResultUsageProvider
{
    private const string SubscriptionEndpoint = "https://api.elevenlabs.io/v1/user/subscription";
    public ProviderDescriptor Descriptor { get; } = new("ElevenLabs", 20, ProviderAuthenticationKind.ApiKey, CredentialNames.ElevenLabs, 8, "elevenlabs", ["elevenlabs_session"]);

    public bool IsConfigured(ProviderQueryContext context) => !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.ElevenLabs));
    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.ElevenLabs);
        if (apiKey is null)
        {
            return null;
        }

        using (var request = new HttpRequestMessage(HttpMethod.Get, SubscriptionEndpoint))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("xi-api-key", apiKey);
            using (var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false))
            {
                var root = document.RootElement;
                var characterCount = ProviderJson.GetDecimal(root, "character_count");
                var characterLimit = ProviderJson.GetDecimal(root, "character_limit");
                var resetUnix = ProviderJson.GetDouble(root, "next_character_count_reset_unix");
                var plan = PlanLabel(ProviderJson.GetString(root, "tier"));
                if (characterCount is null || characterLimit is null || resetUnix is null)
                {
                    throw new ProviderException("ElevenLabs response did not contain character_count, character_limit, and next_character_count_reset_unix.");
                }

                if (characterLimit <= 0)
                {
                    throw new ProviderException("ElevenLabs response contained an invalid character_limit.");
                }

                var usedPercent = (double)(characterCount.Value / characterLimit.Value * 100);
                var resetAt = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(resetUnix.Value));
                var resetText = UsageFormatting.ResetDuration(resetAt - context.Now);
                var window = new UsageWindow(Descriptor.Name, "Session", usedPercent, resetText);
                return new MetricResult(Descriptor.Name, plan, [window]);
            }
        }
    }

    private static string? PlanLabel(string? tier) => string.IsNullOrWhiteSpace(tier) ? null : UsageFormatting.Capitalize(tier.Trim());
}