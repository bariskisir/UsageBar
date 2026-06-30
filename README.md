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
  "criticalPercentage": 95,
  "ELEVENLABS_API_KEY": "",
  "DEEPSEEK_API_KEY": "",
  "OPENROUTER_API_KEY": "",
  "DEEPGRAM_API_KEY": "",
  "iconLayout": {
    "mode": "auto",
    "bars": {}
  },
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

### Tray icon layout

`iconLayout.mode` controls which usage bars are drawn in the tray icon.

Auto mode shows every metric window in provider display order, split equally. If Codex has
Session/Weekly, Claude has Session/Weekly, and ElevenLabs has Session, the icon is split into
five equal bars.

```json
"iconLayout": {
  "mode": "auto",
  "bars": {}
}
```

Manual mode shows only the listed keys, in the same order as the JSON object. Values are layout
percentages.

```json
"iconLayout": {
  "mode": "manual",
  "bars": {
    "codex_session": 25,
    "codex_weekly": 25,
    "claude_session": 25,
    "claude_weekly": 25
  }
}
```

```json
"iconLayout": {
  "mode": "manual",
  "bars": {
    "codex_session": 10,
    "elevenlabs_session": 10
  }
}
```

Available icon layout keys:

- `codex_session`
- `codex_weekly`
- `claude_session`
- `claude_weekly`
- `elevenlabs_session`

## Notifications

Notifications are sent when a usage window crosses a threshold (defaults: 70% high, 95% critical) or resets. Supported channels:

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
