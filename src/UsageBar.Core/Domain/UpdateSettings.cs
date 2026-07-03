using System.Text.Json.Serialization;

namespace UsageBar.Domain;

public sealed record UpdateSettings(
    [property: JsonPropertyName("onStartup")] bool? OnStartup)
{
    public static UpdateSettings Default { get; } = new(true);
}
