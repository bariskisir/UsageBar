# AGENTS.md

This file applies to the entire repository.

## Project

UsageBar is a Windows notification-area application written in Rust. It monitors
LLM/API usage and balances, renders a dynamic tray icon, shows a WebView2 hover
panel, and displays Windows notifications when usage thresholds are crossed.

The Cargo package name is `usagebar`; the user-facing application and binary
name is `UsageBar`.

## Core Rules

- Keep this repository a clean Rust project. Do not reintroduce C#/.NET files,
  `.sln`, `.csproj`, `bin`, `obj`, NuGet artifacts, or old generated output.
- Preserve `.git`, `.gitignore`, `LICENSE`, real user data, secrets,
  certificates, database files, and production/environment-specific files.
- Do not hardcode API keys, tokens, passwords, connection strings, or private
  URLs.
- Prefer small, idiomatic Rust modules with explicit `Result`/`Option` handling.
- Avoid `unwrap`, `expect`, `panic`, and `unsafe` in production paths unless
  there is a clear reason. Windows FFI code is the main justified unsafe area.
- Keep dependencies minimal and mature.

## Architecture

- `src/app/`: application orchestration, aggregation, threshold notifications,
  tooltip card building, and tray icon layout logic.
- `src/config.rs`: settings and application path handling.
- `src/domain.rs`: shared domain structs/enums.
- `src/platform/windows/`: Win32 tray integration, WebView2 tooltip host,
  dynamic tray icon rendering, notifications, startup registration, and context
  menu behavior.
- `src/providers/`: provider implementations. Each provider has its own folder.
- `tests/core/`: core behavior tests.
- `tests/providers/`: provider parsing/client behavior tests.
- `assets/`: installer/runtime visual assets and WebView tooltip HTML/CSS/JS.
- `installer/setup.nsi`: NSIS installer script.

## Providers

Providers are compiled into the application and registered by
`providers::providers(client)`. There is no user-selected provider list and no
custom provider system.

If a provider requires credentials and the user does not provide them through
environment variables or `settings.json`, that provider should simply be
inactive or return no data. Adding a provider should be possible by adding a new
folder under `src/providers/` and registering it in `src/providers/mod.rs`.

## User-Facing Behavior To Preserve

- Product name: `UsageBar`.
- Binary name: `UsageBar.exe`.
- Version is currently `2.0.0`.
- Tray icon is dynamically rendered like the migrated C# application: a 32x32
  dark plate with one or more usage bars.
- Tooltip is a WebView2 HTML/CSS panel and is positioned from the tray icon
  rectangle, not from the cursor hover point.
- Right-click context menu includes refresh interval, high threshold, critical
  threshold, manual refresh, and exit.
- Missing provider credentials should not crash the application.
- Notifications should fire once per threshold cycle, including a critical
  "limit reached" notification when usage moves from below 100% to 100%.

## Configuration

Settings are read from the UsageBar settings file and supported environment
variables. Keep `.env.example` as documentation only; never commit real `.env`
files.

Known provider environment keys:

- `DEEPSEEK_API_KEY`
- `OPENROUTER_API_KEY`
- `DEEPGRAM_API_KEY`

## Build And Checks

Run these before considering work complete:

```powershell
cargo fmt --check
cargo clippy --all-targets --all-features
cargo test
cargo build
```

For release packaging checks on Windows:

```powershell
cargo build --release
```

The GitHub release workflow installs NSIS and builds the installer from
`installer/setup.nsi`.

## GitHub Release Workflow

The release workflow is `.github/workflows/release-desktop.yml`.

It is triggered by tags matching `v*`, builds `UsageBar.exe` on
`windows-latest`, packages it with NSIS, uploads
`artifacts/installer/*_x64-setup.exe`, and publishes a GitHub Release.

Do not change the release asset naming pattern unless explicitly requested.

## Editing Guidance

- Keep edits scoped to the requested behavior.
- Do not delete unrelated files or revert user changes.
- Prefer `apply_patch` for manual edits.
- Keep tests meaningful; avoid placeholder tests.
- If changing Win32 behavior, validate with `cargo check`, `cargo clippy`, and
  a Windows build.
