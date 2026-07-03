using System.Text.Json.Serialization;
using UsageBar.Configuration;

namespace UsageBar.Domain;

public sealed record VisualSettings(
    [property: JsonPropertyName("iconLayout")] TrayIconLayoutSettings? IconLayout)
{
    public static VisualSettings Default { get; } = new(TrayIconLayoutSettings.Default);
}
