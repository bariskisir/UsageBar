using Microsoft.Extensions.Logging.Abstractions;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using UsageBar.Core.Infrastructure;
using Xunit;

namespace UsageBar.Tests;
public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task Legacy_settings_are_backed_up_and_reset_to_v3_defaults()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, "{\"refresh\":{\"minute\":15}}");
            using (var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance))
            {
                var loaded = await store.ReadAsync();
                Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
                Assert.Equal(RefreshSettings.Default.Minute, loaded.Refresh!.Minute);
                Assert.True(File.Exists(Path.Combine(directory, "settings.v2.backup.json")));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Corrupt_settings_are_backed_up_and_reset()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, "not-json");
            using (var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance))
            {
                var loaded = await store.ReadAsync();
                Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
                Assert.True(File.Exists(Path.Combine(directory, "settings.corrupt.json")));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Async_round_trip_is_normalized_and_leaves_no_temporary_file()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            using (var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance))
            {
                var settings = AppSettings.Default with
                {
                    Refresh = new RefreshSettings(0),
                    Providers = [new ProviderSettings("Test", "apiKey", "TEST_KEY", "secret", true)],
                };
                await store.WriteAsync(settings);
                var loaded = await store.ReadAsync();
                Assert.Equal(RefreshSettings.Default.Minute, loaded.Refresh!.Minute);
                Assert.Equal("secret", Assert.Single(loaded.Providers!).ApiKey);
                Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Concurrent_writes_leave_valid_json()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            using (var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance))
            {
                await Task.WhenAll(Enumerable.Range(1, 12).Select(minute => store.WriteAsync(AppSettings.Default with { Refresh = new RefreshSettings(minute) })));
                var loaded = await store.ReadAsync();
                Assert.InRange(loaded.Refresh!.Minute, 1, 12);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"UsageBarTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}