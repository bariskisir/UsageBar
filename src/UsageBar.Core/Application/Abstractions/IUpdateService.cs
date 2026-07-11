namespace UsageBar.Core.Application;

public sealed record UpdateCheckResult(
    bool HasUpdate,
    string? LatestVersion,
    string? ErrorMessage);

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}