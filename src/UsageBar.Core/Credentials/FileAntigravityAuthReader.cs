using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsageBar.Core.Providers;

/// <summary>
/// Reads Antigravity OAuth credentials from the app's data directory. This intentionally uses
/// neither Windows Credential Manager, macOS Keychain, nor Linux Secret Service.
/// </summary>
public sealed class FileAntigravityAuthReader : IAntigravityAuthReader
{
    private readonly Lock _gate = new();
    private readonly string _credentialsFilePath;

    public FileAntigravityAuthReader(string? credentialsFilePath = null) =>
        _credentialsFilePath = credentialsFilePath ?? Infrastructure.PlatformPaths.AntigravityCredentialsFilePath;

    public AntigravityAuth? Read()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(_credentialsFilePath)
                    ? Parse(File.ReadAllText(_credentialsFilePath))
                    : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public void Save(AntigravityAuth auth)
    {
        ArgumentNullException.ThrowIfNull(auth);

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_credentialsFilePath)
                ?? throw new InvalidOperationException("Antigravity credentials path has no parent directory.");
            Directory.CreateDirectory(directory);

            var payload = new CredentialFile(
                new TokenFile(auth.AccessToken, auth.RefreshToken, auth.Expiry?.ToString("o", CultureInfo.InvariantCulture), auth.IdToken),
                "consumer");
            var temporaryPath = _credentialsFilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(payload));
                File.Move(temporaryPath, _credentialsFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private static AntigravityAuth? Parse(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<CredentialFile>(json);
            if (string.IsNullOrWhiteSpace(file?.Token?.AccessToken))
            {
                return null;
            }

            var expiry = DateTimeOffset.TryParse(
                file.Token.Expiry,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedExpiry)
                ? parsedExpiry
                : (DateTimeOffset?)null;
            return new AntigravityAuth(file.Token.AccessToken, file.Token.RefreshToken, expiry, file.Token.IdToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record CredentialFile(
        [property: JsonPropertyName("token")] TokenFile Token,
        [property: JsonPropertyName("auth_method")] string AuthMethod);

    private sealed record TokenFile(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expiry")] string? Expiry,
        [property: JsonPropertyName("id_token")] string? IdToken);
}