namespace UsageBar.Providers;

/// <summary>
/// Canonical credential names for API-key providers. Each value is also the
/// environment-variable name used as a fallback when the settings value is blank.
/// </summary>
public static class CredentialNames
{
    public const string DeepSeek = "DEEPSEEK_API_KEY";
    public const string OpenRouter = "OPENROUTER_API_KEY";
    public const string Moonshot = "MOONSHOT_API_KEY";
    public const string Deepgram = "DEEPGRAM_API_KEY";
    public const string ElevenLabs = "ELEVENLABS_API_KEY";
    public const string Kilo = "KILO_API_KEY";
}
