namespace UsageBar.Domain;

internal interface IUsageProvider
{
    string Name { get; }

    Task<ProviderResult?> GetUsageAsync(ProviderCredentials credentials, CancellationToken cancellationToken);
}

internal sealed record ProviderCredentials(
    string DeepSeekApiKey,
    string OpenRouterApiKey,
    string DeepgramApiKey);

internal sealed record ProviderResult(IReadOnlyList<UsageBlock> Blocks, double? CodexPrimaryUsedPercent = null);

internal sealed record UsageBlock(string Label, string Value, bool Inline = false);
