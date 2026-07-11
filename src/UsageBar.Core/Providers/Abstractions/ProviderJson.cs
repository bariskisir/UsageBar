using System.Globalization;
using System.Text.Json;

namespace UsageBar.Core.Providers;

/// <summary>
/// Tolerant helpers for reading values out of provider JSON responses, accepting
/// numbers encoded as either JSON numbers or strings.
/// </summary>
public static class ProviderJson
{
    public static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var property) ? GetDecimal(property) : null;
    }

    public static decimal? GetDecimal(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    public static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    public static string? GetString(JsonElement element, params string[] propertyNames)
    {
        if (propertyNames is null)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    public static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        property = default;
        return false;
    }
}