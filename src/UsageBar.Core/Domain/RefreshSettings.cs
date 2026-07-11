using System.Text.Json.Serialization;

namespace UsageBar.Core.Domain;

public sealed record RefreshSettings(
    [property: JsonPropertyName("minute")] int Minute)
{
    public const int MaxMinutes = 1440;

    public static RefreshSettings Default { get; } = new(5);
}