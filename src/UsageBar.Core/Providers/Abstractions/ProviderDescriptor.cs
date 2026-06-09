namespace UsageBar.Providers;

/// <summary>
/// Static identity and presentation metadata for a provider. Lets the refresh pipeline order
/// providers without hardcoding their names, so a new provider is fully described by the provider
/// itself (no edits to layout/tooltip/ordering code).
/// </summary>
/// <param name="Name">Display name, e.g. "Codex" or "DeepSeek".</param>
/// <param name="DisplayOrder">
/// Ascending sort key for tray bars and tooltip cards. Convention: metric providers use low values
/// (Codex 0, Claude 10) and balance providers use high values (100+), so metric cards naturally
/// precede balance cards.
/// </param>
public sealed record ProviderDescriptor(string Name, int DisplayOrder);
