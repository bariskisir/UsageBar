using System.Text.Json.Serialization;

namespace UsageBar.Core.Domain;

public sealed record ModelSettings(
    [property: JsonPropertyName("smallModelSelector")] string SmallModelSelector)
{
    public const string DefaultSmallModelSelector = "nano,mini,haiku,lite,flash,oss";

    public static ModelSettings Default { get; } = new(DefaultSmallModelSelector);

    public ModelSettings Normalize() => this with
    {
        SmallModelSelector = string.IsNullOrWhiteSpace(SmallModelSelector)
            ? DefaultSmallModelSelector
            : SmallModelSelector.Trim(),
    };
}
