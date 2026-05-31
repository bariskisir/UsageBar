# UsageBarRust

UsageBarRust is a minimal Windows notification-area app for showing LLM or API usage and balance information.

![UsageBarRust interface](images/interface.png)

---

## Supported LLMs and APIs
- Codex OAuth
- DeepSeek API
- OpenRouter API
- Deepgram API
- New providers will be added soon.

## Install

1. Download the latest release for your platform from [Releases](https://github.com/bariskisir/usagebar/releases/latest).
2. Install or extract the package.
3. Run **UsageBarRust**.

## Configuration

#### Option - 1

C:\Users\USERNAME\AppData\Roaming\UsageBarRust\settings.json

```json
{
  "refreshPeriodMinute": 5,
  "DEEPSEEK_API_KEY": "",
  "OPENROUTER_API_KEY": "",
  "DEEPGRAM_API_KEY": ""
}
```

#### Option - 2
```powershell
[System.Environment]::SetEnvironmentVariable("DEEPSEEK_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("OPENROUTER_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("DEEPGRAM_API_KEY", "API_KEY", "User")
```

Providers with blank or missing API keys will not be enabled

## Development

```bash
git clone https://github.com/bariskisir/usagebarrust.git
cd usagebarrust
cargo build --release
```

## License

MIT
