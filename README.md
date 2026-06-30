# UsageBar

UsageBar is a minimal Windows notification-area app for showing LLM or API usage and balance information.


<img src="images/interface.png" alt="UsageBar interface" width="60%">

---

## Supported LLMs and APIs
- Codex OAuth
- Claude OAuth
- ElevenLabs API
- DeepSeek API
- OpenRouter API
- Deepgram API
- New providers will be added soon.

## Install

1. Download the latest release for your platform from [Releases](https://github.com/bariskisir/usagebar/releases/latest).
2. Install or extract the package.
3. Run **Usage Bar**.

## Configuration

#### Option - 1

C:\Users\USERNAME\AppData\Roaming\UsageBar\settings.json

```json
{
  "refreshPeriodMinute": 5,
  "highPercentage": 70,
  "criticalPercentage": 90,
  "ELEVENLABS_API_KEY": "",
  "DEEPSEEK_API_KEY": "",
  "OPENROUTER_API_KEY": "",
  "DEEPGRAM_API_KEY": "",
  "telegram": {
    "token": "",
    "chatId": 0
  },
  "discord": {
    "webhookUrl": "",
    "username": "Usage Bar"
  }
}
```

#### Option - 2
```powershell
[System.Environment]::SetEnvironmentVariable("DEEPSEEK_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("OPENROUTER_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("DEEPGRAM_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("ELEVENLABS_API_KEY", "API_KEY", "User")
```

Providers with blank or missing API keys will not be enabled

## Notifications

Notifications are sent when a usage window crosses a threshold (defaults: 70% high, 90% critical) or resets. Supported channels:

- **Telegram** — set `telegram.token` and `telegram.chatId`
- **Discord** — set `discord.webhookUrl`


## Development

```bash
git clone https://github.com/bariskisir/usagebar.git
cd usagebar
dotnet run --project src/UsageBar.App
```

## License

MIT — see [LICENSE](LICENSE).
