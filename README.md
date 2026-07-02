# UsageBar

UsageBar is a minimal Windows notification-area app for showing LLM or API usage and balance information.


<img src="images/interface.png" alt="UsageBar interface" width="60%">

<details>
<summary>Multi column</summary>

<img src="images/interface_multi_column.png" alt="UsageBar multi column" width="60%">

</details>

---

## Supported LLMs and APIs
- Codex OAuth
- Claude OAuth
- Antigravity OAuth
- ElevenLabs API
- DeepSeek API
- OpenRouter API
- Moonshot (Kimi) API
- Deepgram API
- Kilo AI API

*The providers below are not tested yet:*

- OpenAI API
- Venice API
- Copilot API
- Crof API
- Codebuff API
- Warp API
- Zai API
- Synthetic API
- Chutes API
- MiniMax API
- Poe API
- Alibaba API

## Install

1. Download the latest release for your platform from [Releases](https://github.com/bariskisir/usagebar/releases/latest).
2. Install or extract the package.
3. Run **Usage Bar**.

## Configuration

#### Option - 1 — Right-click menu

Right-click the tray icon to open the context menu. Most settings can be configured directly from here without editing files:

- **Refresh every** / **High Level** / **Critical Level** — select preset values
- **Provider** — hover to see all providers (API-key and OAuth). Each provider opens a submenu:
  - **Show** — toggle tick to show/hide the provider in the tray icon and tooltip. Hidden providers are stored in `hiddenProviders` in `settings.json`.
  - **API Key** — (API-key providers only) enter or edit the API key. A tick means a key is already configured (from `settings.json` or an environment variable).
  - A tick on the provider name itself means credentials are configured (API key or OAuth session).
- **Telegram** — enter a bot token and numeric chat ID for notifications
- **Discord** — enter a webhook URL and optional custom username
- **Test Notification** — send a test balloon and remote notification
- **Refresh** — trigger an immediate provider refresh

All changes are written to `settings.json` automatically. When editing a value the dialog shows the current setting; leave the field blank to disable it.

#### Option - 2 — settings.json

`C:\Users\USERNAME\AppData\Roaming\UsageBar\settings.json`

```json
{
  "refreshPeriodMinute": 5,
  "highPercentage": 70,
  "criticalPercentage": 95,
  "ELEVENLABS_API_KEY": "",
  "DEEPSEEK_API_KEY": "",
  "OPENROUTER_API_KEY": "",
  "MOONSHOT_API_KEY": "",
  "DEEPGRAM_API_KEY": "",
  "KILO_API_KEY": "",
  "OPENAI_API_KEY": "",
  "VENICE_API_KEY": "",
  "COPILOT_API_KEY": "",
  "CROF_API_KEY": "",
  "CODEBUFF_API_KEY": "",
  "WARP_API_KEY": "",
  "ZAI_API_KEY": "",
  "SYNTHETIC_API_KEY": "",
  "CHUTES_API_KEY": "",
  "MINIMAX_API_KEY": "",
  "POE_API_KEY": "",
  "ALIBABA_API_KEY": "",
  "hiddenProviders": [],
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

#### Option - 3 — Environment variables
```powershell
[System.Environment]::SetEnvironmentVariable("DEEPSEEK_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("OPENROUTER_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("MOONSHOT_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("DEEPGRAM_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("ELEVENLABS_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("KILO_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("VENICE_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("COPILOT_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("CROF_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("CODEBUFF_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("WARP_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("ZAI_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("SYNTHETIC_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("CHUTES_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("MINIMAX_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("POE_API_KEY", "API_KEY", "User")
[System.Environment]::SetEnvironmentVariable("ALIBABA_API_KEY", "API_KEY", "User")
```

Providers with blank or missing API keys will not be enabled

### Provider hiding

Use the right-click menu **Provider** → _provider name_ → **Show** to toggle a provider's visibility.
Hidden providers are not queried or displayed in the tray icon or tooltip. The list is persisted in
`hiddenProviders` in `settings.json`.

### Tray icon layout

`iconLayout.mode` controls which usage bars are drawn in the tray icon.

Auto mode shows every metric window in provider display order, split equally. If Codex has
Session/Weekly, Claude has Session/Weekly, ElevenLabs has Session, and Kilo has Kilo Pass, the
icon is split into six equal bars.

```json
"iconLayout": {
  "mode": "auto",
  "bars": {}
}
```

Manual mode shows only the listed keys, in the same order as the JSON object. Values are layout
percentages. Keys ending with `*` act as a wildcard prefix, matching all windows from that provider
(e.g. `minimax_*` matches every MiniMax model window, `zai_*` matches all Zai limit windows).

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

Wildcard example — `minimax_*` shows every MiniMax model window with equal weight:

```json
"iconLayout": {
  "mode": "manual",
  "bars": {
    "codex_session": 25,
    "codex_weekly": 25,
    "minimax_*": 25,
    "zai_*": 25
  }
}
```

Available icon layout keys (format: `{providername}_{label}`, case-insensitive, spaces become `_`):

- `codex_session`
- `codex_weekly`
- `claude_session`
- `claude_weekly`
- `antigravity_*` — wildcard, matches all Antigravity quota buckets (labels vary by account)
- `elevenlabs_session`
- `kilo_pass`
- `copilot_premium`
- `copilot_chat`
- `warp_requests`
- `synthetic_rolling_5h` · `synthetic_weekly` · `synthetic_search`
- `chutes_4h_rolling` · `chutes_monthly`
- `alibaba_5h` · `alibaba_weekly` · `alibaba_monthly`
- `codebuff_quota`
- `zai_*` — wildcard, matches all Zai windows (labels vary by API response)
- `minimax_*` — wildcard, matches all MiniMax model windows (model names vary by account)

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
