using System.Text.Json.Serialization;

namespace UsageBar.Providers;

[JsonSerializable(typeof(CodexRefreshTokenRequest))]
internal sealed partial class CodexJsonContext : JsonSerializerContext;
