using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class ClaudeAuthReaderTests
{
    [Fact]
    public void Read_parses_valid_auth_file()
    {
        var path = CreateAuthFile("""
        {
          "claudeAiOauth": {
            "accessToken": "at-123",
            "subscriptionType": "pro",
            "rateLimitTier": "claude_max",
            "refreshToken": "rt-456",
            "expiresAt": 2524608000000,
            "scopes": ["user:profile", "rate_limits:read"]
          }
        }
        """);

        try
        {
            var reader = new ClaudeAuthReader(path);
            var auth = reader.Read();

            Assert.NotNull(auth);
            Assert.Equal("at-123", auth!.AccessToken);
            Assert.Equal("pro", auth.SubscriptionType);
            Assert.Equal("claude_max", auth.RateLimitTier);
            Assert.Equal("rt-456", auth.RefreshToken);
            Assert.Equal(new DateTimeOffset(2050, 1, 1, 0, 0, 0, TimeSpan.Zero), auth.ExpiresAt);
            Assert.Equal(["user:profile", "rate_limits:read"], auth.Scopes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_returns_null_when_file_missing()
    {
        var reader = new ClaudeAuthReader(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));
        Assert.Null(reader.Read());
    }

    [Fact]
    public void Read_returns_null_when_access_token_missing()
    {
        var path = CreateAuthFile("""{ "claudeAiOauth": { "subscriptionType": "pro" } }""");
        try
        {
            var reader = new ClaudeAuthReader(path);
            Assert.Null(reader.Read());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_returns_null_when_oauth_key_missing()
    {
        var path = CreateAuthFile("""{ "other": {} }""");
        try
        {
            var reader = new ClaudeAuthReader(path);
            Assert.Null(reader.Read());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_writes_to_new_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            var reader = new ClaudeAuthReader(path);
            reader.Save(new ClaudeAuth("new-token", "max", "claude_max", "new-refresh",
                new DateTimeOffset(2050, 1, 1, 0, 0, 0, TimeSpan.Zero), ["user:profile"]));

            var json = File.ReadAllText(path);
            Assert.Contains("\"accessToken\": \"new-token\"", json, StringComparison.Ordinal);
            Assert.Contains("\"subscriptionType\": \"max\"", json, StringComparison.Ordinal);
            Assert.Contains("\"refreshToken\": \"new-refresh\"", json, StringComparison.Ordinal);
            Assert.Contains("2524608000000", json, StringComparison.Ordinal);
            Assert.Contains("\"scopes\": [", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_preserves_other_top_level_keys()
    {
        var path = CreateAuthFile("""
        {
          "claudeAiOauth": { "accessToken": "old", "subscriptionType": "free" },
          "otherKey": { "value": 42 }
        }
        """);

        try
        {
            var reader = new ClaudeAuthReader(path);
            reader.Save(new ClaudeAuth("new-token", "pro", null, null));

            var json = File.ReadAllText(path);
            Assert.Contains("\"accessToken\": \"new-token\"", json, StringComparison.Ordinal);
            Assert.Contains("\"otherKey\"", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_updates_existing_oauth_object()
    {
        var path = CreateAuthFile("""{ "claudeAiOauth": { "accessToken": "old", "subscriptionType": "free" } }""");
        try
        {
            var reader = new ClaudeAuthReader(path);
            reader.Save(new ClaudeAuth("new-token", "pro", null, null));

            var json = File.ReadAllText(path);
            Assert.Contains("\"accessToken\": \"new-token\"", json, StringComparison.Ordinal);
            Assert.Contains("\"subscriptionType\": \"pro\"", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateAuthFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}