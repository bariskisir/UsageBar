using System.Text.Json.Serialization;
using UsageBar.Core.Configuration;

namespace UsageBar.Core.Settings;

internal sealed record SettingsInboundMessage(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("settings")] AppSettings? Settings,
    [property: JsonPropertyName("envSourcedKeys")] IReadOnlyList<string>? EnvironmentSourcedKeys,
    [property: JsonPropertyName("dx")] int? DeltaX,
    [property: JsonPropertyName("dy")] int? DeltaY);

internal sealed record SettingsStateMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("settings")] AppSettings Settings,
    [property: JsonPropertyName("envApiKeys")] Dictionary<string, string> EnvironmentApiKeys,
    [property: JsonPropertyName("iconLayoutKeys")] IReadOnlyList<string> IconLayoutKeys,
    [property: JsonPropertyName("version")] string Version);

internal sealed record SettingsStatusMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SettingsInboundMessage))]
[JsonSerializable(typeof(SettingsStateMessage))]
[JsonSerializable(typeof(SettingsStatusMessage))]
internal sealed partial class SettingsIpcJsonContext : JsonSerializerContext;