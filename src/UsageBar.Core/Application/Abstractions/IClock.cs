namespace UsageBar.Application;

/// <summary>Abstraction over the system clock for testable time-dependent logic.</summary>
public interface IClock
{
    /// <summary>The current local date and time with offset.</summary>
    DateTimeOffset Now { get; }
}
