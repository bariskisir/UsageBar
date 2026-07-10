namespace UsageBar.Core.Domain;

/// <summary>A single threshold-crossing message to surface to the user.</summary>
public sealed record ThresholdNotification(NotificationLevel Level, string Message);
