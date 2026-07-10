using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
        EnsureFile();
    }

    public async Task<AppSettings> ReadAsync(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureFile();
            var raw = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize(raw, SettingsJsonContext.Default.AppSettings)?.Normalize()
                           ?? AppSettings.Default;
            await WriteNormalizedIfChangedAsync(raw, settings, cancellationToken).ConfigureAwait(false);
            LogRead(settings, started, isAsync: true);
            return settings;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

    public AppSettings Read()
    {
        var started = Stopwatch.GetTimestamp();
        _gate.Wait();
        try
        {
            EnsureFile();
            var raw = File.ReadAllText(_filePath);
            var settings = (JsonSerializer.Deserialize(raw, SettingsJsonContext.Default.AppSettings) ?? AppSettings.Default)
                .Normalize();
            WriteNormalizedIfChanged(raw, settings);
            LogRead(settings, started, isAsync: false);
            return settings;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

    public void Write(AppSettings settings)
    {
        var started = Stopwatch.GetTimestamp();
        _gate.Wait();
        try
        {
            WriteAtomic(Serialize(settings.Normalize()));
            LogWrite(settings, started, isAsync: false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to write settings.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureFile()
    {
        if (!File.Exists(_filePath))
        {
            WriteAtomic(Serialize(AppSettings.Default));
            _logger.LogInformation("Created default settings file.");
        }
    }

    private static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

    private async Task WriteNormalizedIfChangedAsync(
        string raw,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var normalized = Serialize(settings);
        if (!string.Equals(raw, normalized, StringComparison.Ordinal))
        {
            await WriteAtomicAsync(normalized, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Persisted normalized settings defaults.");
        }
    }

    private void WriteNormalizedIfChanged(string raw, AppSettings settings)
    {
        var normalized = Serialize(settings);
        if (!string.Equals(raw, normalized, StringComparison.Ordinal))
        {
            WriteAtomic(normalized);
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

    private void WriteAtomic(string content)
    {
        var temporaryPath = TemporaryPath();
        try
        {
            File.WriteAllText(temporaryPath, content);
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

    private void LogRead(AppSettings settings, long started, bool isAsync) => _logger.LogInformation(
        "Settings loaded: mode={ReadMode}; providerCount={ProviderCount}; enabledProviderCount={EnabledProviderCount}; durationMs={DurationMs:F1}.",
        isAsync ? "async" : "sync",
        settings.Providers?.Count ?? 0,
        settings.Providers?.Count(provider => provider.Enabled) ?? 0,
        Stopwatch.GetElapsedTime(started).TotalMilliseconds);

    private void LogWrite(AppSettings settings, long started, bool isAsync) => _logger.LogInformation(
        "Settings saved: mode={WriteMode}; providerCount={ProviderCount}; enabledProviderCount={EnabledProviderCount}; durationMs={DurationMs:F1}.",
        isAsync ? "async" : "sync",
        settings.Providers?.Count ?? 0,
        settings.Providers?.Count(provider => provider.Enabled) ?? 0,
        Stopwatch.GetElapsedTime(started).TotalMilliseconds);

    public void Dispose() => _gate.Dispose();
}
