using System.Text.Json.Serialization;
using UsageBar.Core.Configuration;

namespace UsageBar.Core.Domain;

public sealed record VisualSettings(
    [property: JsonPropertyName("scale")] int? Scale,
    [property: JsonPropertyName("iconLayout")] TrayIconLayoutSettings? IconLayout)
{
    public static VisualSettings Default { get; } = new(Scale: 100, TrayIconLayoutSettings.Default);
}
