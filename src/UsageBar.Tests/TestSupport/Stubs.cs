using UsageBar.Core.Domain;
using UsageBar.Core.Providers;
using UsageBar.Core.Configuration;
using UsageBar.Core.Application;

namespace UsageBar.Tests;

internal sealed class StubCodexAuthReader(CodexAuth? auth) : ICodexAuthReader
{
    private CodexAuth? _auth = auth;

    public CodexAuth? Saved { get; private set; }

    public CodexAuth? Read() => _auth;

    public void Save(CodexAuth auth)
    {
        Saved = auth;
        _auth = auth;
    }
}

internal sealed class StubClaudeAuthReader(ClaudeAuth? auth) : IClaudeAuthReader
{
    private ClaudeAuth? _auth = auth;

    public ClaudeAuth? Saved { get; private set; }

    public ClaudeAuth? Read() => _auth;

    public void Save(ClaudeAuth auth)
    {
        Saved = auth;
        _auth = auth;
    }
}

/// <summary>A provider whose result (or exception) is supplied by a delegate.</summary>
internal sealed class StubProvider(string name, Func<ProviderResult?> result, int displayOrder = 0) : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new(name, displayOrder);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult(result());
}

internal sealed class DynamicOrderProvider(
    string name,
    Func<ProviderResult?> result,
    int metricOrder,
    int balanceOrder) : IUsageProvider, IResultDisplayOrderProvider
{
    public ProviderDescriptor Descriptor { get; } = new(name, DisplayOrder: 0);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult(result());

    public int GetDisplayOrder(ProviderResult providerResult) => providerResult switch
    {
        MetricResult => metricOrder,
        BalanceResult => balanceOrder,
        _ => Descriptor.DisplayOrder,
    };
}

internal sealed class StubSettingsStore(AppSettings settings) : ISettingsStore
{
    public AppSettings Current { get; set; } = settings;

    public Task<AppSettings> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task WriteAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        return Task.CompletedTask;
    }

    public AppSettings Read() => Current;

    public void Write(AppSettings settings) => Current = settings;
}

internal static class TestData
{
    public static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static ProviderQueryContext Context(params (string Name, string Value)[] apiKeys)
    {
        var dictionary = apiKeys.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return new ProviderQueryContext(FixedNow, dictionary, new Dictionary<string, bool>());
    }

    public static UsageWindow Window(string provider, string label, double percent, string? reset = null) =>
        new(provider, label, percent, reset);
}
