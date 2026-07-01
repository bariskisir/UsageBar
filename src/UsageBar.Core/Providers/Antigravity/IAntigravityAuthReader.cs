namespace UsageBar.Providers;

/// <summary>Reads and persists Antigravity OAuth credentials.</summary>
public interface IAntigravityAuthReader
{
    /// <summary>Returns the current auth, or <see langword="null"/> when unavailable.</summary>
    AntigravityAuth? Read();

    /// <summary>Persists refreshed OAuth material back to the credential store.</summary>
    void Save(AntigravityAuth auth);
}
