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

internal sealed record ProviderResult(
    IReadOnlyList<UsageBlock> Blocks,
    IReadOnlyList<UsageBarWindow> Windows = null!)
{
    public static ProviderResult FromLegacy(
        IReadOnlyList<UsageBlock> blocks,
        double? codexPrimaryUsedPercent,
        double? codexSecondaryUsedPercent)
    {
        var windows = new List<UsageBarWindow>();
        if (codexPrimaryUsedPercent is not null)
        {
            windows.Add(new UsageBarWindow("Codex", "5h", codexPrimaryUsedPercent.Value));
        }

        if (codexSecondaryUsedPercent is not null)
        {
            windows.Add(new UsageBarWindow("Codex", "7d", codexSecondaryUsedPercent.Value));
        }

        return new ProviderResult(blocks, windows);
    }

    public double? CodexPrimaryUsedPercent
    {
        get
        {
            foreach (var w in Windows ?? [])
            {
                if (w.ProviderName == "Codex" && w.WindowLabel == "5h")
                {
                    return w.UsedPercent;
                }
            }

            return null;
        }
    }

    public double? CodexSecondaryUsedPercent
    {
        get
        {
            foreach (var w in Windows ?? [])
            {
                if (w.ProviderName == "Codex" && w.WindowLabel == "7d")
                {
                    return w.UsedPercent;
                }
            }

            return null;
        }
    }
}

internal sealed record UsageBarWindow(string ProviderName, string WindowLabel, double UsedPercent);

internal sealed record UsageBlock(string Label, string Value, bool Inline = false);
