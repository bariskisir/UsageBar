namespace UsageBar.Core.Infrastructure;

internal interface IStartupRegistrationService
{
    void Register();
    void Unregister();
}