using UsageBar.Domain;
using UsageBar.Providers;

namespace UsageBar.Tests;

internal sealed class StubCodexAuthReader(CodexAuth? auth) : ICodexAuthReader
{
    public CodexAuth? Read() => auth;
}

internal sealed class StubClaudeAuthReader(ClaudeAuth? auth) : IClaudeAuthReader
{
    public ClaudeAuth? Read() => auth;
}

/// <summary>A provider whose result (or exception) is supplied by a delegate.</summary>
internal sealed class StubProvider(string name, ProviderCategory category, Func<ProviderResult?> result) : IUsageProvider
{
    public string Name => name;

    public ProviderCategory Category => category;

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult(result());
}

internal static class TestData
{
    public static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static ProviderQueryContext Context(params (string Name, string Value)[] apiKeys)
    {
        var dictionary = apiKeys.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return new ProviderQueryContext(FixedNow, dictionary);
    }

    public static UsageWindow Window(string provider, string label, double percent, string? reset = null) =>
        new(provider, label, percent, reset);
}
