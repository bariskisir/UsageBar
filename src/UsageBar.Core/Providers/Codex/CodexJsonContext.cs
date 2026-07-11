using System.Text.Json.Serialization;

namespace UsageBar.Core.Providers;

[JsonSerializable(typeof(CodexRefreshTokenRequest))]
internal sealed partial class CodexJsonContext : JsonSerializerContext;