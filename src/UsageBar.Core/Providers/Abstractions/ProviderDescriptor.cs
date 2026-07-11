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
        string[]? IconLayoutKeys = null,
        string? Id = null)
    {
        this.Id = string.IsNullOrWhiteSpace(Id) ? CreateId(Name) : Id;
        this.Name = Name;
        this.DisplayOrder = DisplayOrder;
        this.AuthenticationKind = AuthenticationKind;
        this.CredentialName = CredentialName;
        this.SettingsOrder = SettingsOrder;
        this.IconKey = IconKey;
        this.IconLayoutKeys = IconLayoutKeys ?? [];
    }

    /// <summary>Stable, presentation-independent provider identity used by v3 settings.</summary>
    public string Id { get; }

    /// <summary>Display name, e.g. "Codex" or "DeepSeek".</summary>
    public string Name { get; }

    /// <summary>Ascending sort key for tray bars and tooltip cards.</summary>
    public int DisplayOrder { get; }

    public ProviderAuthenticationKind AuthenticationKind { get; }

    public string? CredentialName { get; }

    public int SettingsOrder { get; }

    public string? IconKey { get; }

    public IReadOnlyList<string> IconLayoutKeys { get; }

    private static string CreateId(string name)
    {
        var id = string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        if (id.Length == 0)
        {
            throw new ArgumentException("Provider name must contain at least one letter or digit.", nameof(name));
        }

        return id;
    }

}