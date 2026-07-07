using System.Text.Json.Serialization;
using UsageBar.Configuration;

namespace UsageBar.Domain;

public sealed record VisualSettings(
    [property: JsonPropertyName("scale")] int? Scale,
    [property: JsonPropertyName("iconLayout")] TrayIconLayoutSettings? IconLayout)
{
    public static VisualSettings Default { get; } = new(Scale: 100, TrayIconLayoutSettings.Default);
}
