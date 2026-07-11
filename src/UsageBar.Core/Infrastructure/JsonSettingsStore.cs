using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using UsageBar.Core.Application;
using UsageBar.Core.Configuration;

namespace UsageBar.Core.Infrastructure;
/// <summary>Serialized, atomically-written JSON settings persistence.</summary>
internal sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    private readonly string _filePath;
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public JsonSettingsStore(string filePath, ILogger<JsonSettingsStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public async Task<AppSettings> ReadAsync(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureFileAsync(cancellationToken).ConfigureAwait(false);
            var raw = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            var settings = await DeserializeOrResetAsync(raw, cancellationToken).ConfigureAwait(false);
            await WriteNormalizedIfChangedAsync(raw, settings, cancellationToken).ConfigureAwait(false);
            LogRead(settings, started, isAsync: true);
            return settings;
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read settings; using defaults.");
            return AppSettings.Default;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAtomicAsync(Serialize(settings.Normalize()), cancellationToken).ConfigureAwait(false);
            LogWrite(settings, started, isAsync: true);
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to write settings.");
            throw new InvalidOperationException("Settings could not be persisted.", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            await WriteAtomicAsync(Serialize(AppSettings.Default), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created default settings file.");
        }
    }

    private static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
    private async Task<AppSettings> DeserializeOrResetAsync(string raw, CancellationToken cancellationToken)
    {
        if (TryDeserializeV3(raw, out var settings))
        {
            return settings;
        }

        BackupLegacyOrCorrupt(raw);
        await WriteAtomicAsync(Serialize(AppSettings.Default), cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Reset settings for schema version {SchemaVersion}.", AppSettings.CurrentSchemaVersion);
        return AppSettings.Default;
    }

    private static bool TryDeserializeV3(string raw, out AppSettings settings)
    {
        settings = AppSettings.Default;
        try
        {
            using (var document = JsonDocument.Parse(raw))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion) || schemaVersion.ValueKind != JsonValueKind.Number || schemaVersion.GetInt32() != AppSettings.CurrentSchemaVersion)
                {
                    return false;
                }

                var parsed = JsonSerializer.Deserialize(raw, SettingsJsonContext.Default.AppSettings);
                if (parsed?.SchemaVersion != AppSettings.CurrentSchemaVersion)
                {
                    return false;
                }

                settings = parsed.Normalize();
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void BackupLegacyOrCorrupt(string raw)
    {
        var isLegacy = IsLegacyJson(raw);
        var suffix = isLegacy ? "v2.backup" : "corrupt";
        var preferred = Path.Combine(Path.GetDirectoryName(_filePath)!, $"settings.{suffix}.json");
        var backupPath = File.Exists(preferred) ? Path.Combine(Path.GetDirectoryName(_filePath)!, $"settings.{suffix}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json") : preferred;
        File.Copy(_filePath, backupPath, overwrite: false);
        _logger.LogWarning("Backed up incompatible settings to {BackupPath}.", backupPath);
    }

    private static bool IsLegacyJson(string raw)
    {
        try
        {
            using (var document = JsonDocument.Parse(raw))
            {
                return document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty("schemaVersion", out _);
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task WriteNormalizedIfChangedAsync(string raw, AppSettings settings, CancellationToken cancellationToken)
    {
        var normalized = Serialize(settings);
        if (!string.Equals(raw, normalized, StringComparison.Ordinal))
        {
            await WriteAtomicAsync(normalized, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Persisted normalized settings defaults.");
        }
    }

    private async Task WriteAtomicAsync(string content, CancellationToken cancellationToken)
    {
        var temporaryPath = TemporaryPath();
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private string TemporaryPath() => $"{_filePath}.{Guid.NewGuid():N}.tmp";
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        // Best-effort cleanup; the destination write outcome is already known.
        }
    }

    private void LogRead(AppSettings settings, long started, bool isAsync) => _logger.LogInformation("Settings loaded: mode={ReadMode}; providerCount={ProviderCount}; enabledProviderCount={EnabledProviderCount}; durationMs={DurationMs:F1}.", isAsync ? "async" : "sync", settings.Providers?.Count ?? 0, settings.Providers?.Count(provider => provider.Enabled) ?? 0, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    private void LogWrite(AppSettings settings, long started, bool isAsync) => _logger.LogInformation("Settings saved: mode={WriteMode}; providerCount={ProviderCount}; enabledProviderCount={EnabledProviderCount}; durationMs={DurationMs:F1}.", isAsync ? "async" : "sync", settings.Providers?.Count ?? 0, settings.Providers?.Count(provider => provider.Enabled) ?? 0, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    public void Dispose() => _gate.Dispose();
}