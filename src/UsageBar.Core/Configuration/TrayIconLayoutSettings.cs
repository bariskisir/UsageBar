using System.Text.Json.Serialization;

namespace UsageBar.Configuration;

public sealed record TrayIconLayoutSettings(
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("bars")] Dictionary<string, double>? Bars)
{
    public const string AutoMode = "auto";
    public const string ManualMode = "manual";

    public static TrayIconLayoutSettings Default { get; } = new(AutoMode, []);

    [JsonIgnore]
    public bool IsManual => string.Equals(Mode, ManualMode, StringComparison.OrdinalIgnoreCase);

    public TrayIconLayoutSettings Normalize()
    {
        var mode = string.Equals(Mode, ManualMode, StringComparison.OrdinalIgnoreCase)
            ? ManualMode
            : AutoMode;

        var bars = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (Bars is not null)
        {
            foreach (var (key, value) in Bars)
            {
                if (!string.IsNullOrWhiteSpace(key) && double.IsFinite(value) && value > 0)
                {
                    bars[key.Trim()] = value;
                }
            }
        }

        return new TrayIconLayoutSettings(mode, bars);
    }
}
