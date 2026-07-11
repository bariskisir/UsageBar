namespace UsageBar.Core.Providers;

/// <summary>Reads Claude OAuth credentials from the local Claude credentials file.</summary>
public interface IClaudeAuthReader
{
    /// <summary>Returns the current auth, or <see langword="null"/> when unavailable/incomplete.</summary>
    ClaudeAuth? Read();

    /// <summary>Persists refreshed OAuth material back to the local Claude credentials file.</summary>
    void Save(ClaudeAuth auth);
}