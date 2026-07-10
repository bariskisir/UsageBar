using System.Net.Http.Headers;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Reports MiniMax model-based usage percentages and points balance.</summary>
public sealed class MiniMaxProvider(HttpClient httpClient) : IUsageProvider, IResultDisplayOrderProvider
{
    private const string InternationalEndpoint = "https://api.minimax.io/v1/token_plan/remains";
    private const string ChinaEndpoint = "https://api.minimaxi.com/v1/token_plan/remains";

    public ProviderDescriptor Descriptor { get; } = new(
        "MiniMax", 25, ProviderAuthenticationKind.ApiKey, CredentialNames.MiniMax, 19, "minimax", ["minimax_*"]);

    public int GetDisplayOrder(ProviderResult result) => result switch
    {
        MetricResult => 25,
        BalanceResult => 130,
        _ => Descriptor.DisplayOrder,
    };

    public bool IsConfigured(ProviderQueryContext context) =>
        !string.IsNullOrEmpty(context.GetApiKey(CredentialNames.MiniMax));

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialNames.MiniMax);
        if (apiKey is null)
        {
            return null;
        }

        // Try international endpoint first, fall back to China.
        using var request = new HttpRequestMessage(HttpMethod.Get, InternationalEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("MM-API-Source", "CodexBar");

        System.Text.Json.JsonDocument document;
        try
        {
            document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            using var chinaRequest = new HttpRequestMessage(HttpMethod.Get, ChinaEndpoint);
            chinaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            chinaRequest.Headers.TryAddWithoutValidation("MM-API-Source", "CodexBar");
            document = await ProviderHttp.GetJsonAsync(httpClient, chinaRequest, cancellationToken).ConfigureAwait(false);
        }

        var root = document.RootElement;
        var data = root;
        if (ProviderJson.TryGetProperty(root, "data", out var dataProp) && dataProp.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            data = dataProp;
        }

        // Points balance (shown as plan line).
        var pointsBalance = ProviderJson.GetDecimal(data, "points_balance");
        var pointsText = pointsBalance is not null
            ? $"{pointsBalance.Value:0.00} pts"
            : null;

        // Model-level usage windows.
        var windows = new List<UsageWindow>();
        if (ProviderJson.TryGetProperty(data, "model_remains", out var modelRemains) &&
            modelRemains.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var model in modelRemains.EnumerateArray())
            {
                var window = ReadModelRemain(model, Descriptor.Name, context.Now);
                if (window is not null)
                {
                    windows.Add(window);
                }
            }
        }

        // If only points balance, return as balance result.
        if (windows.Count == 0 && pointsText is not null)
        {
            return new BalanceResult(Descriptor.Name, pointsText);
        }

        if (windows.Count == 0)
        {
            throw new ProviderException("MiniMax response did not contain usable model usage windows.");
        }

        return new MetricResult(Descriptor.Name, pointsText, windows);
    }

    private static UsageWindow? ReadModelRemain(System.Text.Json.JsonElement model, string providerName, DateTimeOffset now)
    {
        var modelName = ProviderJson.GetString(model, "model_name", "model", "name") ?? "Model";
        var remainingPercent = ProviderJson.GetDouble(model, "current_interval_remaining_percent");

        double? usedPercent = null;
        if (remainingPercent is not null)
        {
            usedPercent = Math.Clamp(100.0 - remainingPercent.Value, 0, 100);
        }
        else
        {
            var usageCount = ProviderJson.GetDecimal(model, "current_interval_usage_count");
            var totalCount = ProviderJson.GetDecimal(model, "current_interval_total_count");
            if (usageCount is not null && totalCount is not null && totalCount.Value > 0)
            {
                usedPercent = (double)(usageCount.Value / totalCount.Value * 100);
            }
        }

        if (usedPercent is null)
        {
            return null;
        }

        var endTime = ProviderJson.GetString(model, "end_time");
        string? resetText = null;
        if (endTime is not null &&
            DateTimeOffset.TryParse(endTime, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            resetText = UsageFormatting.ResetDuration(parsed - now);
        }

        return new UsageWindow(providerName, modelName, Math.Clamp(usedPercent.Value, 0, 100), resetText);
    }
}
