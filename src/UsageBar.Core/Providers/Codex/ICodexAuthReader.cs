namespace UsageBar.Providers;

/// <summary>Codex OAuth material required to query usage.</summary>
public sealed record CodexAuth(string AccessToken, string AccountId);

/// <summary>Reads Codex OAuth credentials from the local Codex CLI auth file.</summary>
public interface ICodexAuthReader
{
    /// <summary>Returns the current auth, or <see langword="null"/> when unavailable/incomplete.</summary>
    CodexAuth? Read();
}
