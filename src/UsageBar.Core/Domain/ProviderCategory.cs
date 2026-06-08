namespace UsageBar.Domain;

/// <summary>
/// Distinguishes the two kinds of providers Usage Bar supports.
/// </summary>
public enum ProviderCategory
{
    /// <summary>Reports one or more usage windows (used percent + reset), e.g. Codex, Claude.</summary>
    Metric,

    /// <summary>Reports a single remaining balance, e.g. DeepSeek, OpenRouter, Deepgram.</summary>
    Balance,
}
