using System.Globalization;
using System.Text.Json;

namespace UsageBar.Providers;

/// <summary>
/// Reads and persists Antigravity OAuth credentials from Windows Credential Manager.
/// The Gemini CLI keeps the credential store current.
/// </summary>
public sealed class AntigravityAuthReader : IAntigravityAuthReader
{
    private const string CredentialTarget = "gemini:antigravity";

    private readonly Lock _gate = new();

    public AntigravityAuthReader() { }

    public AntigravityAuth? Read()
    {
        lock (_gate)
        {
            var credJson = WindowsCredentialManager.Read(CredentialTarget);
            if (string.IsNullOrWhiteSpace(credJson))
            {
                return null;
            }

            return ParseCredentialManagerJson(credJson);
        }
    }

    public void Save(AntigravityAuth auth)
    {
        lock (_gate)
        {
            SaveToCredentialManager(auth);
        }
    }

    private static AntigravityAuth? ParseCredentialManagerJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!ProviderJson.TryGetProperty(root, "token", out var token) || token.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var accessToken = ProviderJson.GetString(token, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            return new AntigravityAuth(
                accessToken,
                ProviderJson.GetString(token, "refresh_token"),
                ParseExpiry(ProviderJson.GetString(token, "expiry")),
                IdToken: ProviderJson.GetString(token, "id_token"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SaveToCredentialManager(AntigravityAuth auth)
    {
        Dictionary<string, JsonElement>? existing = null;
        try
        {
            var raw = WindowsCredentialManager.Read(CredentialTarget);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw);
            }
        }
        catch { }

        var tokenObj = new Dictionary<string, JsonElement>();
        if (existing is not null &&
            existing.TryGetValue("token", out var existingToken) &&
            existingToken.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in existingToken.EnumerateObject())
            {
                tokenObj[prop.Name] = prop.Value;
            }
        }

        tokenObj["access_token"] = JsonSerializer.SerializeToElement(auth.AccessToken);
        tokenObj["token_type"] = JsonSerializer.SerializeToElement("Bearer");

        if (!string.IsNullOrWhiteSpace(auth.RefreshToken))
        {
            tokenObj["refresh_token"] = JsonSerializer.SerializeToElement(auth.RefreshToken);
        }

        if (auth.Expiry is { } expiry)
        {
            tokenObj["expiry"] = JsonSerializer.SerializeToElement(expiry.ToString("o"));
        }

        if (!string.IsNullOrWhiteSpace(auth.IdToken))
        {
            tokenObj["id_token"] = JsonSerializer.SerializeToElement(auth.IdToken);
        }

        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["token"] = tokenObj,
            ["auth_method"] = existing is not null &&
                              existing.TryGetValue("auth_method", out var am) &&
                              am.ValueKind == JsonValueKind.String &&
                              am.GetString() is { } method
                ? method
                : "consumer",
        });

        if (!WindowsCredentialManager.Write(CredentialTarget, json))
        {
            throw new InvalidOperationException(
                $"CredWrite failed for target '{CredentialTarget}'.");
        }
    }

    private static DateTimeOffset? ParseExpiry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
