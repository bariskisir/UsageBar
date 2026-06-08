namespace UsageBar.Domain;

/// <summary>
/// Severity of a threshold notification, selecting the built-in Windows balloon glyph.
/// </summary>
public enum NotificationLevel
{
    /// <summary>Usage dropped — positive event, shows the info symbol.</summary>
    Reset,

    /// <summary>Crossed the high threshold — shows the warning symbol.</summary>
    High,

    /// <summary>Crossed the critical threshold — shows the error (critical) symbol.</summary>
    Critical,
}
