using System.Text.Json.Serialization;

namespace UsageBar.Core.Domain;

public interface IRemoteNotificationSettings
{
    bool IsEnabled { get; }
}
