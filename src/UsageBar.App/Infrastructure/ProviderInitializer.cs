using UsageBar.Application;
using UsageBar.Configuration;
using UsageBar.Domain;
using UsageBar.Providers;

namespace UsageBar.Infrastructure;

internal sealed class ProviderInitializer(
    ISettingsStore settingsStore,
    ICodexAuthReader codexAuth,
    IClaudeAuthReader claudeAuth,
    IAntigravityAuthReader antigravityAuth)
{
    private static readonly (string Name, string? Credential, string Type)[] Providers =
    [
        ("Codex", null, ProviderSettings.TypeOAuth),
        ("Claude", null, ProviderSettings.TypeOAuth),
        ("Antigravity", null, ProviderSettings.TypeOAuth),
        ("DeepSeek", CredentialNames.DeepSeek, ProviderSettings.TypeApiKey),
        ("OpenRouter", CredentialNames.OpenRouter, ProviderSettings.TypeApiKey),
        ("ZenMux", CredentialNames.ZenMux, ProviderSettings.TypeApiKey),
        ("Moonshot (Kimi)", CredentialNames.Moonshot, ProviderSettings.TypeApiKey),
        ("Deepgram", CredentialNames.Deepgram, ProviderSettings.TypeApiKey),
        ("ElevenLabs", CredentialNames.ElevenLabs, ProviderSettings.TypeApiKey),
        ("Kilo", CredentialNames.Kilo, ProviderSettings.TypeApiKey),
        ("OpenAI", CredentialNames.OpenAI, ProviderSettings.TypeApiKey),
        ("Venice", CredentialNames.Venice, ProviderSettings.TypeApiKey),
        ("Copilot", CredentialNames.Copilot, ProviderSettings.TypeApiKey),
        ("Crof", CredentialNames.Crof, ProviderSettings.TypeApiKey),
        ("Codebuff", CredentialNames.Codebuff, ProviderSettings.TypeApiKey),
        ("Warp", CredentialNames.Warp, ProviderSettings.TypeApiKey),
        ("Zai", CredentialNames.Zai, ProviderSettings.TypeApiKey),
        ("Synthetic", CredentialNames.Synthetic, ProviderSettings.TypeApiKey),
        ("Chutes", CredentialNames.Chutes, ProviderSettings.TypeApiKey),
        ("MiniMax", CredentialNames.MiniMax, ProviderSettings.TypeApiKey),
        ("Poe", CredentialNames.Poe, ProviderSettings.TypeApiKey),
        ("Alibaba", CredentialNames.Alibaba, ProviderSettings.TypeApiKey)
    ];

    public void EnsureProviders()
    {
        var settings = settingsStore.Read();

        if (settings.Initialized != true)
        {
            var providers = new List<ProviderSettings>();
            foreach (var (name, credential, type) in Providers)
            {
                providers.Add(new ProviderSettings(
                    Name: name,
                    Type: type,
                    Credential: credential,
                    ApiKey: null,
                    Enabled: HasCredential(name, credential)));
            }

            settingsStore.Write(settings with { Providers = providers, Initialized = true });
            return;
        }

        var existingProviders = settings.Providers ?? [];
        var byName = new Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in existingProviders)
            byName[p.Name] = p;

        var merged = new List<ProviderSettings>(existingProviders);
        var changed = false;

        foreach (var (name, credential, type) in Providers)
        {
            if (byName.ContainsKey(name))
                continue;

            merged.Add(new ProviderSettings(
                Name: name,
                Type: type,
                Credential: credential,
                ApiKey: null,
                Enabled: HasCredential(name, credential)));
            changed = true;
        }

        if (changed)
        {
            settingsStore.Write(settings with { Providers = merged, Initialized = true });
        }
    }

    private bool HasCredential(string providerName, string? credentialName)
    {
        if (credentialName is not null)
        {
            var envValue = Environment.GetEnvironmentVariable(credentialName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                return true;
            }
        }

        return providerName switch
        {
            "Codex" => TryReadAuth(() => codexAuth.Read()?.AccessToken),
            "Claude" => TryReadAuth(() => claudeAuth.Read()?.AccessToken),
            "Antigravity" => TryReadAuth(() => antigravityAuth.Read()?.AccessToken),
            _ => false,
        };
    }

    private static bool TryReadAuth(Func<string?> read)
    {
        try
        {
            return !string.IsNullOrEmpty(read());
        }
        catch
        {
            return false;
        }
    }
}
