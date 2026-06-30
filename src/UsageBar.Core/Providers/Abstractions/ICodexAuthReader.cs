namespace UsageBar.Providers;

/// <summary>Reads Codex OAuth credentials from the local Codex CLI auth file.</summary>
public interface ICodexAuthReader
{
    /// <summary>Returns the current auth, or <see langword="null"/> when unavailable/incomplete.</summary>
    CodexAuth? Read();

    /// <summary>Persists refreshed OAuth material back to the local Codex auth file.</summary>
    void Save(CodexAuth auth);
}
