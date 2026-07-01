namespace UsageBar.Providers;

/// <summary>
/// Static identity and presentation metadata for a provider. Lets the refresh pipeline order
/// providers without hardcoding their names, so a new provider is fully described by the provider
/// itself (no edits to layout/tooltip/ordering code).
/// </summary>
public sealed class ProviderDescriptor
{
    /// <param name="name">Display name, e.g. "Codex" or "DeepSeek".</param>
    /// <param name="displayOrder">
    /// Ascending sort key for tray bars and tooltip cards. Convention: metric providers use low
    /// values (Codex 0, Claude 10) and balance providers use high values (100+), so metric cards
    /// naturally precede balance cards.
    /// </param>
    public ProviderDescriptor(string Name, int DisplayOrder)
    {
        this.Name = Name;
        this.DisplayOrder = DisplayOrder;
    }

    /// <summary>Display name, e.g. "Codex" or "DeepSeek".</summary>
    public string Name { get; }

    /// <summary>Ascending sort key for tray bars and tooltip cards.</summary>
    public int DisplayOrder { get; }

    /// <summary>
    /// Set to <see langword="false"/> by a provider when it detects that credentials are
    /// missing (no API key, no auth file, etc.). The aggregator resets this to
    /// <see langword="true"/> at the start of each refresh so providers that gain
    /// credentials mid-session are automatically re-enabled.
    /// </summary>
    public bool IsEnabled { get; set; }
}
