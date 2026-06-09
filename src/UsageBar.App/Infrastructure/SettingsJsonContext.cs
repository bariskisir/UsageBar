using System.Text.Json.Serialization;
using UsageBar.Configuration;

namespace UsageBar.Infrastructure;

/// <summary>
/// System.Text.Json source-generation context for <see cref="AppSettings"/>. Using generated
/// metadata instead of run-time reflection keeps (de)serialization fast and trim/AOT-safe. JSON
/// is written indented (human-editable settings file) and read case-insensitively.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
