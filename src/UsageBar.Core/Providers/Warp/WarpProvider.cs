using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;
/// <summary>Reports Warp request limit usage via GraphQL.</summary>
public sealed class WarpProvider(HttpClient httpClient) : ISingleResultUsageProvider
{
    private const string GraphQLEndpoint = "https://app.warp.dev/graphql/v2?op=GetRequestLimitInfo";
    public ProviderDescriptor Descriptor { get; } = new("Warp", 13, ProviderAuthenticationKind.ApiKey, CredentialNames.Warp, 15, "warp", ["warp_requests"]);

    public bool IsConfigured(ProviderQueryContext context) => !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.Warp));
    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.Warp);
        if (apiKey is null)
        {
            return null;
        }

        var query = """{"query":"query GetRequestLimitInfo($requestContext: RequestContext!) { user(requestContext: $requestContext) { __typename ... on UserOutput { user { requestLimitInfo { isUnlimited nextRefreshTime requestLimit requestsUsedSinceLastRefresh } bonusGrants { requestCreditsGranted requestCreditsRemaining expiration } workspaces { bonusGrantsInfo { grants { requestCreditsGranted requestCreditsRemaining expiration } } } } } } }","variables":{"requestContext":{"clientContext":{},"osContext":{"category":"macOS","name":"macOS","version":"15.0"}}},"operationName":"GetRequestLimitInfo"}""";
        using (var request = new HttpRequestMessage(HttpMethod.Post, GraphQLEndpoint)
        {
            Content = new StringContent(query, Encoding.UTF8, "application/json"),
        }

        )
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.TryAddWithoutValidation("x-warp-client-id", "warp-app");
            request.Headers.TryAddWithoutValidation("x-warp-os-category", "macOS");
            request.Headers.TryAddWithoutValidation("x-warp-os-name", "macOS");
            request.Headers.TryAddWithoutValidation("User-Agent", "Warp/1.0");
            using (var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false))
            {
                var root = document.RootElement;
                // Check for GraphQL errors.
                if (ProviderJson.TryGetProperty(root, "errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                {
                    throw new ProviderException("Warp GraphQL errors returned.");
                }

                // Navigate: data.user.user.requestLimitInfo
                var info = root;
                foreach (var key in new[]
                {
                    "data",
                    "user",
                    "user",
                    "requestLimitInfo"
                }

                )
                {
                    if (!ProviderJson.TryGetProperty(info, key, out var next))
                    {
                        throw new ProviderException("Warp response did not contain requestLimitInfo.");
                    }

                    info = next;
                }

                var isUnlimited = IsTruthy(ProviderJson.GetString(info, "isUnlimited"));
                if (isUnlimited)
                {
                    // Still parse bonus grants for unlimited users.
                    var bonusPlan = ParseBonusPlan(root);
                    return new MetricResult(Descriptor.Name, bonusPlan ?? "Unlimited", [new UsageWindow(Descriptor.Name, "Requests", 0, null)]);
                }

                var requestLimit = ProviderJson.GetDouble(info, "requestLimit");
                var requestsUsed = ProviderJson.GetDouble(info, "requestsUsedSinceLastRefresh");
                if (requestLimit is null || requestsUsed is null || requestLimit.Value <= 0)
                {
                    throw new ProviderException("Warp response did not contain valid request limit info.");
                }

                var usedPercent = (requestsUsed.Value / requestLimit.Value * 100);
                var nextRefresh = ProviderJson.GetString(info, "nextRefreshTime");
                string? resetText = null;
                if (nextRefresh is not null && DateTimeOffset.TryParse(nextRefresh, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    resetText = UsageFormatting.ResetDuration(parsed - context.Now);
                }

                var plan = ParseBonusPlan(root);
                var window = new UsageWindow(Descriptor.Name, "Requests", Math.Clamp(usedPercent, 0, 100), resetText);
                return new MetricResult(Descriptor.Name, plan, [window]);
            }
        }
    }

    private static string? ParseBonusPlan(JsonElement root)
    {
        var userObj = root;
        foreach (var key in new[]
        {
            "data",
            "user",
            "user"
        }

        )
        {
            if (!ProviderJson.TryGetProperty(userObj, key, out var next))
            {
                return null;
            }

            userObj = next;
        }

        var totalGranted = 0.0;
        var totalRemaining = 0.0;
        // User-level bonus grants.
        if (ProviderJson.TryGetProperty(userObj, "bonusGrants", out var bonusGrants) && bonusGrants.ValueKind == JsonValueKind.Array)
        {
            foreach (var grant in bonusGrants.EnumerateArray())
            {
                totalGranted += AsDouble(grant, "requestCreditsGranted");
                totalRemaining += AsDouble(grant, "requestCreditsRemaining");
            }
        }

        // Workspace-level bonus grants.
        if (ProviderJson.TryGetProperty(userObj, "workspaces", out var workspaces) && workspaces.ValueKind == JsonValueKind.Array)
        {
            foreach (var workspace in workspaces.EnumerateArray())
            {
                if (ProviderJson.TryGetProperty(workspace, "bonusGrantsInfo", out var bonusGrantsInfo) && ProviderJson.TryGetProperty(bonusGrantsInfo, "grants", out var wsGrants) && wsGrants.ValueKind == JsonValueKind.Array)
                {
                    foreach (var grant in wsGrants.EnumerateArray())
                    {
                        totalGranted += AsDouble(grant, "requestCreditsGranted");
                        totalRemaining += AsDouble(grant, "requestCreditsRemaining");
                    }
                }
            }
        }

        if (totalGranted > 0)
        {
            return $"+{(int)totalRemaining}/{(int)totalGranted} bonus";
        }

        return null;
    }

    private static double AsDouble(JsonElement element, string propertyName)
    {
        if (!ProviderJson.TryGetProperty(element, propertyName, out var prop))
        {
            return 0;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var d))
        {
            return d;
        }

        if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
        {
            return s;
        }

        return 0;
    }

    private static bool IsTruthy(string? value) => value is not null && (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1");
}