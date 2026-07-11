using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using UsageBar.Core.Providers;

namespace UsageBar.MacOS.Infrastructure;
public sealed class AntigravityAuthReader : IAntigravityAuthReader
{
    private const string ServiceName = "gemini:antigravity";
    private const string AccountName = "antigravity";
    private static readonly string FallbackPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".antigravity", "auth.json");
    private readonly Lock _gate = new();
    public AntigravityAuth? Read()
    {
        lock (_gate)
        {
            var json = ReadFromKeychain();
            if (!string.IsNullOrWhiteSpace(json))
            {
                return ParseCredentialJson(json);
            }

            var fileFallback = new FileAntigravityAuthReader(FallbackPath).Read();
            if (fileFallback is not null)
            {
                Save(fileFallback);
            }

            return fileFallback;
        }
    }

    public void Save(AntigravityAuth auth)
    {
        lock (_gate)
        {
            SaveToKeychain(auth);
        }
    }

    private static string? ReadFromKeychain()
    {
        try
        {
            using (var process = Process.Start(new ProcessStartInfo { FileName = "security", ArgumentList = { "find-generic-password", "-s", ServiceName, "-a", AccountName, "-w", }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, }))
            {
                if (process is null)
                {
                    return null;
                }

                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                    return null;
                }

                return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : null;
            }
        }
        catch
        {
            return null;
        }
    }

    private void SaveToKeychain(AntigravityAuth auth)
    {
        var json = SerializeAuth(auth);
        try
        {
            RemoveFromKeychain();
            using (var process = Process.Start(new ProcessStartInfo { FileName = "security", ArgumentList = { "add-generic-password", "-s", ServiceName, "-a", AccountName, "-w", json, "-U", }, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, }))
            {
                if (process is null)
                {
                    throw new InvalidOperationException("Failed to start security.");
                }

                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                    throw new InvalidOperationException("security add-generic-password timed out.");
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"security add-generic-password failed with exit code {process.ExitCode}.");
                }
            }
        }
        catch (Exception ex)when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Failed to store Antigravity credential in Keychain.", ex);
        }
    }

    private static void RemoveFromKeychain()
    {
        try
        {
            using (var process = Process.Start(new ProcessStartInfo { FileName = "security", ArgumentList = { "delete-generic-password", "-s", ServiceName, }, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, }))
            {
                if (process is not null && !process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
        }
    }

    private static AntigravityAuth? ParseCredentialJson(string json)
    {
        try
        {
            using (var document = JsonDocument.Parse(json))
            {
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

                return new AntigravityAuth(accessToken, ProviderJson.GetString(token, "refresh_token"), ParseExpiry(ProviderJson.GetString(token, "expiry")), IdToken: ProviderJson.GetString(token, "id_token"));
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SerializeAuth(AntigravityAuth auth)
    {
        var tokenObj = new JsonObject
        {
            ["access_token"] = auth.AccessToken,
            ["token_type"] = "Bearer",
        };
        if (!string.IsNullOrWhiteSpace(auth.RefreshToken))
        {
            tokenObj["refresh_token"] = auth.RefreshToken;
        }

        if (auth.Expiry is { } expiry)
        {
            tokenObj["expiry"] = expiry.ToString("o");
        }

        if (!string.IsNullOrWhiteSpace(auth.IdToken))
        {
            tokenObj["id_token"] = auth.IdToken;
        }

        var root = new JsonObject
        {
            ["token"] = tokenObj,
            ["auth_method"] = "consumer",
        };
        return root.ToJsonString();
    }

    private static DateTimeOffset? ParseExpiry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
    }
}