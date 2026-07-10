namespace UsageBar.Core.Providers;

public enum ProviderAuthenticationKind
{
    None,
    OAuth,
    ApiKey,
}

/// <summary>
/// Static identity and presentation metadata for a provider. Lets the refresh pipeline order
/// providers without hardcoding their names, so a new provider is fully described by the provider
/// itself (no edits to layout/tooltip/ordering code).
/// </summary>
public sealed record ProviderDescriptor
{
    /// <param name="name">Display name, e.g. "Codex" or "DeepSeek".</param>
    /// <param name="displayOrder">
    /// Ascending sort key for tray bars and tooltip cards. Convention: metric providers use low
    /// values (Codex 0, Claude 10) and balance providers use high values (100+), so metric cards
    /// naturally precede balance cards.
    /// </param>
    public ProviderDescriptor(
        string Name,
        int DisplayOrder,
        ProviderAuthenticationKind AuthenticationKind = ProviderAuthenticationKind.None,
        string? CredentialName = null,
        int SettingsOrder = int.MaxValue,
        string? IconKey = null,
        string[]? IconLayoutKeys = null)
    {
        this.Name = Name;
        this.DisplayOrder = DisplayOrder;
        this.AuthenticationKind = AuthenticationKind;
        this.CredentialName = CredentialName;
        this.SettingsOrder = SettingsOrder;
        this.IconKey = IconKey;
        this.IconLayoutKeys = IconLayoutKeys ?? [];
    }

    /// <summary>Display name, e.g. "Codex" or "DeepSeek".</summary>
    public string Name { get; }

    /// <summary>Ascending sort key for tray bars and tooltip cards.</summary>
    public int DisplayOrder { get; }

    public ProviderAuthenticationKind AuthenticationKind { get; }

    public string? CredentialName { get; }

    public int SettingsOrder { get; }

    public string? IconKey { get; }

    public IReadOnlyList<string> IconLayoutKeys { get; }

}
