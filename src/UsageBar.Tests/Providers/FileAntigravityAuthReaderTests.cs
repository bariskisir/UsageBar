using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests.Providers;

public sealed class FileAntigravityAuthReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "UsageBarTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_then_read_round_trips_the_app_data_credential_shape()
    {
        var path = Path.Combine(_directory, "antigravity", "oauth_creds.json");
        var reader = new FileAntigravityAuthReader(path);
        var expiry = DateTimeOffset.Parse("2026-07-10T12:00:00Z");

        reader.Save(new AntigravityAuth("access-token", "refresh-token", expiry, "id-token"));

        var auth = Assert.IsType<AntigravityAuth>(reader.Read());
        Assert.Equal("access-token", auth.AccessToken);
        Assert.Equal("refresh-token", auth.RefreshToken);
        Assert.Equal(expiry, auth.Expiry);
        Assert.Equal("id-token", auth.IdToken);
        var json = File.ReadAllText(path);
        Assert.Contains("\"access_token\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CredentialManager", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_returns_null_for_missing_or_malformed_file()
    {
        var path = Path.Combine(_directory, "oauth_creds.json");
        var reader = new FileAntigravityAuthReader(path);

        Assert.Null(reader.Read());
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{");
        Assert.Null(reader.Read());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
