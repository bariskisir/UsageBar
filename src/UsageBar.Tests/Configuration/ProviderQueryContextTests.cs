using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class ProviderQueryContextTests
{
    private static List<ProviderSettings> Provider(string name, string? apiKey)
    {
        var type = ProviderSettings.TypeApiKey;
        string? credential = name switch
        {
            "DeepSeek" => CredentialNames.DeepSeek,
            "OpenRouter" => CredentialNames.OpenRouter,
            "Moonshot (Kimi)" => CredentialNames.Moonshot,
            "Deepgram" => CredentialNames.Deepgram,
            "ElevenLabs" => CredentialNames.ElevenLabs,
            "Kilo" => CredentialNames.Kilo,
            "ZenMux" => CredentialNames.ZenMux,
            _ => null,
        };
        return [new ProviderSettings(name, type, credential, apiKey, Enabled: false)];
    }

    [Fact]
    public void Settings_value_takes_precedence()
    {
        var settings = AppSettings.Default with { Providers = Provider("DeepSeek", "from-settings") };
        var context = ProviderQueryContext.FromSettings(settings, TestData.FixedNow, Environment.GetEnvironmentVariable);
        Assert.Equal("from-settings", context.GetApiKey(CredentialNames.DeepSeek));
    }

    [Fact]
    public void Falls_back_to_environment_variable_when_blank()
    {
        var settings = AppSettings.Default with { Providers = Provider("OpenRouter", "") };
        Environment.SetEnvironmentVariable(CredentialNames.OpenRouter, "from-env");
        try
        {
            var context = ProviderQueryContext.FromSettings(settings, TestData.FixedNow, Environment.GetEnvironmentVariable);
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
        var settings = AppSettings.Default with { Providers = Provider("Deepgram", "") };
        Environment.SetEnvironmentVariable(CredentialNames.Deepgram, null);
        var context = ProviderQueryContext.FromSettings(settings, TestData.FixedNow, Environment.GetEnvironmentVariable);
        Assert.Null(context.GetApiKey(CredentialNames.Deepgram));
    }

    [Fact]
    public void ElevenLabs_falls_back_to_environment_variable_when_blank()
    {
        var settings = AppSettings.Default with { Providers = Provider("ElevenLabs", "") };
        Environment.SetEnvironmentVariable(CredentialNames.ElevenLabs, "eleven-env");
        try
        {
            var context = ProviderQueryContext.FromSettings(settings, TestData.FixedNow, Environment.GetEnvironmentVariable);
            Assert.Equal("eleven-env", context.GetApiKey(CredentialNames.ElevenLabs));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CredentialNames.ElevenLabs, null);
        }
    }

    [Fact]
    public void Moonshot_falls_back_to_environment_variable_when_blank()
    {
        var settings = AppSettings.Default with { Providers = Provider("Moonshot (Kimi)", "") };
        Environment.SetEnvironmentVariable(CredentialNames.Moonshot, "moonshot-env");
        try
        {
            var context = ProviderQueryContext.FromSettings(settings, TestData.FixedNow, Environment.GetEnvironmentVariable);
            Assert.Equal("moonshot-env", context.GetApiKey(CredentialNames.Moonshot));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CredentialNames.Moonshot, null);
        }
    }

    [Fact]
    public void Kilo_falls_back_to_environment_variable_when_blank()
    {
        var settings = AppSettings.Default with { Providers = Provider("Kilo", "") };
        Environment.SetEnvironmentVariable(CredentialNames.Kilo, "kilo-env");
        try
        {
            var context = ProviderQueryContext.FromSettings(settings, TestData.FixedNow, Environment.GetEnvironmentVariable);
            Assert.Equal("kilo-env", context.GetApiKey(CredentialNames.Kilo));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CredentialNames.Kilo, null);
        }
    }

    [Fact]
    public void ZenMux_falls_back_to_environment_variable_when_blank()
    {
        var settings = AppSettings.Default with { Providers = Provider("ZenMux", "") };
        Environment.SetEnvironmentVariable(CredentialNames.ZenMux, "zenmux-env");
        try
        {
            var context = ProviderQueryContext.FromSettings(settings, TestData.FixedNow, Environment.GetEnvironmentVariable);
            Assert.Equal("zenmux-env", context.GetApiKey(CredentialNames.ZenMux));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CredentialNames.ZenMux, null);
        }
    }
}