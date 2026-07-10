namespace UsageBar.Core.Domain;

/// <summary>
/// Raised by providers when an API call fails or returns an unexpected response shape.
/// The aggregator catches and logs these so one provider cannot break the refresh.
/// </summary>
public sealed class ProviderException : Exception
{
    public ProviderException(string message)
        : base(message)
    {
    }

    public ProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
