# UsageBarRust

UsageBarRust is a system-tray application for Windows that displays LLM and API usage/balance information.
Originally ported from C#: https://github.com/bariskisir/UsageBar

<img src="images/interface.png" alt="UsageBarRust interface" width="60%">

---

## Supported LLMs and APIs
- Codex OAuth
- Claude OAuth
- DeepSeek API
- OpenRouter API
- Deepgram API
- New providers will be added soon.

## Install

1. Download the latest release for your platform from [Releases](https://github.com/bariskisir/usagebarrust/releases/latest).
2. Install or extract the package.
3. Run **UsageBarRust**.

## Configuration

#### Option - 1

C:\Users\USERNAME\AppData\Roaming\UsageBarRust\settings.json

```json
{
  "refreshPeriodMinute": 5,
  "useLegacyTooltip": false,
  "highPercentage": 70,
  "criticalPercentage": 90,
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

## Notifications

When usage climbs above the configured thresholds, a Windows toast notification is shown:

- **High usage** — usage crosses `highPercentage` (default 70 %) upward.
- **Critical usage** — usage crosses `criticalPercentage` (default 90 %) upward.
- **Limit reset** — when usage drops back below the threshold, the tracking state resets
  so a fresh notification can fire on the next upward crossing.

Each threshold fires only once per cycle (no repeat spam). The thresholds can be changed
from the tray icon's right-click menu (`High Level` / `Critical Level`) or by editing
`settings.json`.

## Development

```bash
git clone https://github.com/bariskisir/usagebarrust.git
cd usagebarrust
cargo build --release
```

## License

MIT
