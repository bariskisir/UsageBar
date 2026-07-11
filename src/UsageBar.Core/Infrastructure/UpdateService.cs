using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using UsageBar.Core.Application;

namespace UsageBar.Core.Infrastructure;
internal sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;
    private readonly Version? _currentVersion;
    private const string ReleasesApi = "https://api.github.com/repos/bariskisir/usagebar/releases/latest";
    private const string UserAgent = "UsageBar";
    public UpdateService(HttpClient httpClient, ILogger<UpdateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _currentVersion = ReadCurrentVersion();
    }

    private static Version? ReadCurrentVersion()
    {
        var versionStr = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (TryParseVersion(versionStr, out var version))
        {
            return version;
        }

        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        if (assemblyVersion is not null && assemblyVersion.Major > 0)
        {
            return assemblyVersion;
        }

        try
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(Process.GetCurrentProcess().MainModule!.FileName!).FileVersion;
            if (TryParseVersion(fileVersion, out var fv) && fv.Major > 0)
            {
                return fv;
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool TryParseVersion(string? raw, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Version? result)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            result = null;
            return false;
        }

        raw = raw.TrimStart('v', 'V');
        var plusIndex = raw.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex > 0)
        {
            raw = raw[..plusIndex];
        }

        return Version.TryParse(raw, out result);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_currentVersion is null)
        {
            return new UpdateCheckResult(false, null, "Could not determine current app version.");
        }

        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi))
            {
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    {
                        using (var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false))
                        {
                            var tagName = document.RootElement.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
                            if (string.IsNullOrWhiteSpace(tagName))
                            {
                                return new UpdateCheckResult(false, null, "GitHub response did not contain tag_name.");
                            }

                            var latestVersionStr = tagName.TrimStart('v');
                            if (!Version.TryParse(latestVersionStr, out var latestVersion))
                            {
                                return new UpdateCheckResult(false, tagName, $"Could not parse version from tag: {tagName}");
                            }

                            _logger.LogInformation("Update check: current={Current}, latest={Latest}", _currentVersion, latestVersion);
                            var hasUpdate = latestVersion > _currentVersion;
                            return new UpdateCheckResult(hasUpdate, tagName, null);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(false, null, "Update check was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed.");
            return new UpdateCheckResult(false, null, ex.Message);
        }
    }
}