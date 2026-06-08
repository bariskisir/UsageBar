using UsageBar.Configuration;
using UsageBar.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class ProviderQueryContextTests
{
    [Fact]
    public void Settings_value_takes_precedence()
    {
        var settings = AppSettings.Default with { DeepSeekApiKey = "from-settings" };

        var context = ProviderQueryContext.FromSettings(settings, TestData.FixedNow);

        Assert.Equal("from-settings", context.GetApiKey(CredentialNames.DeepSeek));
    }

    [Fact]
    public void Falls_back_to_environment_variable_when_blank()
    {
        var settings = AppSettings.Default with { OpenRouterApiKey = "" };
        Environment.SetEnvironmentVariable(CredentialNames.OpenRouter, "from-env");
        try
        {
            var context = ProviderQueryContext.FromSettings(settings, TestData.FixedNow);
            Assert.Equal("from-env", context.GetApiKey(CredentialNames.OpenRouter));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CredentialNames.OpenRouter, null);
        }
    }

    [Fact]
    public void Returns_null_when_blank_and_no_environment_variable()
    {
        var settings = AppSettings.Default with { DeepgramApiKey = "" };
        Environment.SetEnvironmentVariable(CredentialNames.Deepgram, null);

        var context = ProviderQueryContext.FromSettings(settings, TestData.FixedNow);

        Assert.Null(context.GetApiKey(CredentialNames.Deepgram));
    }
}
