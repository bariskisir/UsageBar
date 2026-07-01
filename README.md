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

#### Option - 1

C:\Users\USERNAME\AppData\Roaming\UsageBar\settings.json

```json
{
  "refreshPeriodMinute": 5,
  "highPercentage": 70,
  "criticalPercentage": 95,
  "balanceHidingThreshold": -1,
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

### Balance hiding threshold

`balanceHidingThreshold` hides balance providers (DeepSeek, OpenRouter, Moonshot, Deepgram, Kilo,
OpenAI, Venice, Crof, Poe) from the tooltip when their remaining balance in USD is at or below the configured value.
Defaults to `-1` (disabled — all balance providers are shown).

- Set to `0` to hide providers whose balance is $0.00 or less.
- Set to `0.01` to hide providers with $0.01 or less.
- Decimal values are supported (e.g. `0.50`, `1.00`).

```json
"balanceHidingThreshold": 0
```

For DeepSeek, which reports both USD and CNY balances, the card is only hidden when **both**
balances are at or below the threshold.

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
