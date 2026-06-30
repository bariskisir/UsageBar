using System.Text.RegularExpressions;
using UsageBar.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class CodexAuthReaderTests
{
    [Fact]
    public void Save_writes_last_refresh_in_codex_cli_utc_format()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "tokens": {
                    "access_token": "old-token",
                    "refresh_token": "old-refresh",
                    "account_id": "account"
                  },
                  "last_refresh": "2026-06-06T07:36:29.224884200Z"
                }
                """);

            var reader = new CodexAuthReader(path);
            reader.Save(new CodexAuth("new-token", "account", "new-refresh"));

            var json = File.ReadAllText(path);

            Assert.DoesNotContain(@"\u002B00:00", json, StringComparison.Ordinal);
            Assert.DoesNotContain("+00:00", json, StringComparison.Ordinal);
            Assert.Matches(
                new Regex("\"last_refresh\": \"\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{7}Z\""),
                json);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
