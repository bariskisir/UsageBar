using System.Text.Json.Serialization;

namespace UsageBar.Domain;

public interface IRemoteNotificationSettings
{
    bool IsEnabled { get; }
}
