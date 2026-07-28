# UsageBar

UsageBar is a minimal Windows, macOS, and Linux tray app for showing LLM or API usage and balance information.


<img src="images/interface.png" alt="UsageBar interface" width="60%">

<details>
<summary>Multi column</summary>

<img src="images/interface_multi_column.png" alt="UsageBar multi column" width="60%">

</details>

<img src="images/interface_linux.png" alt="UsageBar interface on Linux" width="60%">

---

## Supported LLMs and APIs
- Codex OAuth
- Claude OAuth
- Antigravity OAuth
- Command Code API
- ElevenLabs API
- DeepSeek API
- OpenRouter API
- ZenMux API
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

## Features

- **Refresh token** — automatically refreshes supported OAuth credentials when they expire.
- **Warm Window** — optionally sends a minimal request to Codex, Claude, and Antigravity after a session reset. Models are discovered dynamically and selected with the shared Small Model Selector preference.

## Install

### Windows

Download the x64 or ARM64 installer for your device from
[Releases](https://github.com/bariskisir/usagebar/releases/latest). Run the installer, then launch
**Usage Bar** from the Start menu. Windows release files use the
`UsageBar-<version>_<architecture>-setup.exe` naming format.

### Linux

Download the x86_64 or ARM64 AppImage for your device from
[Releases](https://github.com/bariskisir/usagebar/releases/latest). Linux release files use the
`UsageBar-<version>_<architecture>.AppImage` naming format. Mark the AppImage as executable if
necessary, then launch it directly; no extraction or installation is required. The .NET runtime is
included, while GTK 3 and WebKitGTK 4.1 remain native system dependencies and must be available on
the host.

#### GNOME

GNOME does not provide a StatusNotifier/AppIndicator tray host by default. To display UsageBar beside
the clock, GNOME users need an AppIndicator-compatible Shell extension, such as **AppIndicator and
KStatusNotifierItem Support**, enabled for the current user. The exact extension or package name
depends on the distribution. After enabling it, GNOME Shell may require a logout and login before
the tray icon appears.

Desktops that already provide a StatusNotifier host do not need the GNOME extension. Without any
tray host, UsageBar displays a small fallback window so its actions remain accessible.

## Configuration

Right-click the tray icon and select **Settings** to open the settings panel.


## Notifications

Notifications are sent when a usage window crosses a threshold (defaults: 70% high, 90% critical) or resets. Supported channels:

- **Telegram** — set `notification.telegram.token` and `notification.telegram.chatId`, toggle `enabled`
- **Discord** — set `notification.discord.webhookUrl`, toggle `enabled`


## Development

Clone the repository, then run the project for your platform.

### Windows

```bash
git clone https://github.com/bariskisir/usagebar.git
cd usagebar
dotnet run --project src/UsageBar.Windows
```

### Linux

```bash
git clone https://github.com/bariskisir/usagebar.git
cd usagebar
dotnet run --project src/UsageBar.Linux
```

## License

MIT — see [LICENSE](LICENSE).
