using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace UsageBar.Core.Infrastructure.Logging;
internal static class SensitiveDataRedactor
{
    private const int MaxSnapshotCharacters = 4096;
    private const int MaxDepth = 6;
    private const int MaxArrayItems = 5;
    private const string Redacted = "<redacted>";
    private static readonly string[] SensitiveNameFragments = ["authorization", "api-key", "apikey", "api_key", "token", "secret", "password", "credential", "cookie", "webhook", "account", "project", "organization", "user_id", "userid", "email", "chat_id", "chatid", "fingerprint", ];
    private static readonly HashSet<string> SafeStringProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "type",
        "unit",
        "currency",
        "status",
        "state",
        "window",
        "grant_type",
        "operationName",
    };
    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    public static string SafeUri(Uri? uri)
    {
        if (uri is null)
        {
            return "<unknown>";
        }

        var path = uri.AbsolutePath;
        if (uri.Host.Contains("discord", StringComparison.OrdinalIgnoreCase))
        {
            path = "/<redacted-webhook>";
        }
        else if (uri.Host.Equals("api.telegram.org", StringComparison.OrdinalIgnoreCase))
        {
            var slash = path.IndexOf('/', 1);
            path = slash < 0 ? "/bot<redacted>" : "/bot<redacted>" + path[slash..];
        }

        var queryNames = QueryNames(uri.Query);
        return $"{uri.Scheme}://{uri.Host}{path}{queryNames}";
    }

    public static string HeaderNames(HttpHeaders headers, HttpHeaders? contentHeaders = null)
    {
        var names = headers.Select(header => header.Key).Concat(contentHeaders?.Select(header => header.Key) ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        return string.Join(',', names);
    }

    public static string BodyFingerprint(string body)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    public static string BodySnapshot(string body, string? mediaType)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "<empty>";
        }

        try
        {
            if (mediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true || LooksLikeJson(body))
            {
                using (var document = JsonDocument.Parse(body))
                {
                    var builder = new StringBuilder(Math.Min(body.Length, MaxSnapshotCharacters));
                    AppendJson(builder, document.RootElement, propertyName: null, depth: 0);
                    return Truncate(builder.ToString());
                }
            }

            if (mediaType?.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Truncate(SanitizeForm(body));
            }
        }
        catch (JsonException)
        {
        // Invalid JSON is described by metadata and fingerprint only.
        }

        return $"<non-json:{body.Length.ToString(CultureInfo.InvariantCulture)} chars>";
    }

    private static void AppendJson(StringBuilder builder, JsonElement element, string? propertyName, int depth)
    {
        if (depth >= MaxDepth)
        {
            builder.Append('"').Append("<max-depth>").Append('"');
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var firstProperty = true;
                foreach (var property in element.EnumerateObject())
                {
                    if (!firstProperty)
                    {
                        builder.Append(',');
                    }

                    firstProperty = false;
                    AppendQuoted(builder, property.Name);
                    builder.Append(':');
                    if (IsSensitive(property.Name))
                    {
                        AppendQuoted(builder, Redacted);
                    }
                    else
                    {
                        AppendJson(builder, property.Value, property.Name, depth + 1);
                    }

                    if (builder.Length >= MaxSnapshotCharacters)
                    {
                        break;
                    }
                }

                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    if (index == MaxArrayItems)
                    {
                        AppendQuoted(builder, "<truncated-array>");
                        break;
                    }

                    AppendJson(builder, item, propertyName, depth + 1);
                    index++;
                    if (builder.Length >= MaxSnapshotCharacters)
                    {
                        break;
                    }
                }

                builder.Append(']');
                break;
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Number:
                AppendQuoted(builder, Redacted);
                break;
            case JsonValueKind.String when propertyName is not null && SafeStringProperties.Contains(propertyName):
                AppendQuoted(builder, element.GetString() ?? string.Empty);
                break;
            default:
                AppendQuoted(builder, Redacted);
                break;
        }
    }

    private static string SanitizeForm(string body)
    {
        var fields = body.Split('&', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('&', fields.Select(field =>
        {
            var separator = field.IndexOf('=');
            var rawName = separator < 0 ? field : field[..separator];
            var name = Uri.UnescapeDataString(rawName.Replace('+', ' '));
            if (name.Equals("grant_type", StringComparison.OrdinalIgnoreCase) && separator >= 0)
            {
                var value = Uri.UnescapeDataString(field[(separator + 1)..].Replace('+', ' '));
                return $"{name}={value}";
            }

            return $"{name}={Redacted}";
        }));
    }

    private static bool IsSensitive(string name) => SensitiveNameFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    private static string QueryNames(string query)
    {
        if (string.IsNullOrEmpty(query) || query == "?")
        {
            return string.Empty;
        }

        var names = query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2)[0]).Select(Uri.UnescapeDataString).Distinct(StringComparer.OrdinalIgnoreCase);
        return $"?{string.Join('&', names.Select(name => name + "=<redacted>"))}";
    }

    private static bool LooksLikeJson(string body)
    {
        var trimmed = body.AsSpan().TrimStart();
        return !trimmed.IsEmpty && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    private static void AppendQuoted(StringBuilder builder, string value)
    {
        builder.Append(JsonSerializer.Serialize(value, LogJsonOptions));
    }

    private static string Truncate(string value) => value.Length <= MaxSnapshotCharacters ? value : value[..MaxSnapshotCharacters] + "<truncated>";
}