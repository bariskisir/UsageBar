using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using UsageBar.Core.Providers;

namespace UsageBar.Linux.Infrastructure;
public sealed class AntigravityAuthReader : IAntigravityAuthReader
{
    private static readonly string FallbackPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".antigravity", "auth.json");
    private readonly Lock _gate = new();
    public AntigravityAuth? Read()
    {
        lock (_gate)
        {
            var json = ReadFromLibsecret();
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
            SaveToLibsecret(auth);
        }
    }

    private static string? ReadFromLibsecret()
    {
        try
        {
            using (var process = Process.Start(new ProcessStartInfo { FileName = "secret-tool", ArgumentList = { "lookup", "application", "antigravity", "target", "gemini:antigravity" }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, }))
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

    private void SaveToLibsecret(AntigravityAuth auth)
    {
        var json = SerializeAuth(auth);
        try
        {
            using (var process = Process.Start(new ProcessStartInfo { FileName = "secret-tool", ArgumentList = { "store", "--label=gemini:antigravity", "application", "antigravity", "target", "gemini:antigravity" }, RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, }))
            {
                if (process is null)
                {
                    throw new InvalidOperationException("Failed to start secret-tool.");
                }

                process.StandardInput.Write(json);
                process.StandardInput.Close();
                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                    throw new InvalidOperationException("secret-tool store timed out.");
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"secret-tool store failed with exit code {process.ExitCode}.");
                }
            }
        }
        catch (Exception ex)when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Failed to store Antigravity credential in libsecret.", ex);
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