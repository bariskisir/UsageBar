# AGENTS.md

## Purpose

This file provides repository-specific instructions for AI coding agents and human contributors working on UsageBarRust. Keep changes focused, verify assumptions against the repository, and update this document when commands, architecture, dependencies, or conventions change.

## Repository Overview

UsageBarRust is a Windows notification-area (system tray) application that displays LLM/API usage and balance information. It is a Rust port of the C# [UsageBar](https://github.com/bariskisir/UsageBar) project. The tray panel UI (tooltip design, icon colour palette, CSS, and bar rendering) is taken from [Win-CodexBar](https://github.com/Finesssee/Win-CodexBar) (MIT-licensed).

- Language/runtime: Rust (edition 2021) on Tokio async runtime.
- Target platform: Windows only (`#![windows_subsystem = "windows"]`, Win32 tray icon via raw FFI).
- Application type: Windows GUI executable (no console window in release builds).
- UI model: raw Win32 tray icon and hidden message window through `extern "system"` FFI in `shell/native.rs`; tooltip popup rendered with **WebView2** via the `wry` crate (v0.54), using a tooltip-only subset of the Win-CodexBar tray styles.
- Providers: Codex OAuth, Claude OAuth, DeepSeek API, OpenRouter API, Deepgram API.
- Configuration path: `%APPDATA%\UsageBarRust\settings.json`.
- Log path: `%APPDATA%\UsageBarRust\app.log`.
- Packaging: No installer yet; distributed as a standalone `cargo build --release` binary.
- CI/CD: None configured yet; release builds are done locally.

## Project Structure

```text
.
|-- .editorconfig
|-- .gitignore
|-- AGENTS.md
|-- Cargo.toml
|-- Cargo.lock
|-- README.md
|-- build.rs
|-- assets/
|   |-- AppIcon.ico
|   |-- usagebar.css        ← tooltip-only subset of CodexBar tray styles
|   `-- tooltip.js           ← DOM builder: MenuCard / MetricRow class hierarchy
|-- images/
|   `-- interface.png
`-- src/
    |-- main.rs
    |-- domain.rs
    |-- application/
    |   |-- mod.rs
    |   |-- aggregator.rs
    |   |-- host.rs
    |   `-- tooltip.rs
    |-- infrastructure/
    |   |-- mod.rs
    |   |-- logger.rs
    |   |-- paths.rs
    |   |-- settings.rs
    |   `-- startup.rs
    |-- providers/
    |   |-- mod.rs
    |   |-- claude.rs
    |   |-- codex.rs
    |   |-- deepgram.rs
    |   |-- deepseek.rs
    |   |-- json_helpers.rs
    |   `-- openrouter.rs
    `-- shell/
        |-- mod.rs
        |-- icon.rs
        |-- native.rs
        |-- tray.rs
        `-- webview_tooltip.rs  ← WebView2 popup for the CodexBar-style tooltip
```

Important files and directories:

| Path | Purpose |
| ---- | ------- |
| `Cargo.toml` | Crate manifest. Defines the `UsageBarRust` binary (v1.3.0), dependencies, and release profile (`opt-level=z`, LTO, single codegen unit, stripped). |
| `build.rs` | Embeds the application icon (`assets/AppIcon.ico`) via `winres` on Windows builds. |
| `src/main.rs` | Entry point. Sets per-monitor DPI awareness (`SetProcessDpiAwarenessContext`), builds a multi-threaded Tokio runtime, calls `rt.enter()`, creates `UsageBarRustHost`, and runs the message loop. Fatal startup failures are written to `app.log` through `FatalErrorLogger`. |
| `src/domain.rs` | `IUsageProvider` trait, `ProviderCredentials`, `ProviderResult`, `UsageBarWindow`, `UsageBlock`, `TooltipCard`, `TooltipMetric`. Pure data — no I/O dependencies. |
| `src/application/host.rs` | `UsageBarRustHost`: wires providers, settings, and tray. `RefreshCoordinator`: periodic refresh loop + threshold-crossing notification logic on Tokio worker threads. |
| `src/application/aggregator.rs` | Parallel provider fan-out with a 45-second aggregate timeout via `CancellationToken`. Collects per-provider plan names into `UsageSnapshot.plans`. |
| `src/application/tooltip.rs` | `format` — 127-char legacy tooltip. `build_cards` — builds `TooltipCard` values for the WebView2 tooltip, mapping windows to display labels (`"5h"` → `"Session"`, `"7d"` → `"Weekly"`). |
| `src/infrastructure/settings.rs` | `SettingsService` reads/writes `%APPDATA%\UsageBarRust\settings.json`. `AppSettings` normalizes values and resolves credentials. Fields: `refreshPeriodMinute`, `useLegacyTooltip`, `highPercentage`, `criticalPercentage`, API keys. `read_sync()` and `write_sync()` are available for the UI thread. |
| `src/infrastructure/paths.rs` | `%APPDATA%\UsageBarRust` path helpers — `settings_file_path()` and `log_file_path()`. |
| `src/infrastructure/logger.rs` | `AppLogger` (async, semaphore-gated file append) and `FatalErrorLogger` (sync, for startup crashes before async infra is ready). |
| `src/infrastructure/startup.rs` | Registers `UsageBarRust` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` via `winreg`. Best-effort — failures are logged, never crash. |
| `src/providers/claude.rs` | Claude OAuth provider. Reads `%USERPROFILE%\.claude\.credentials.json` (`claudeAiOauth.accessToken`, `subscriptionType`/`rateLimitTier`). Calls `https://api.anthropic.com/api/oauth/usage`. Returns plan label (Max/Pro/Team/etc.) alongside `five_hour` and `seven_day` usage windows. |
| `src/providers/codex.rs` | Codex OAuth provider. Reads `%USERPROFILE%\.codex\auth.json`. Calls `https://chatgpt.com/backend-api/wham/usage`. Returns plan label (Free/Plus/Pro/etc.) from `plan_type` alongside 5h and 7d usage windows. |
| `src/providers/deepseek.rs` | DeepSeek API-key provider. Calls `https://api.deepseek.com/user/balance`; displays USD total balance. |
| `src/providers/openrouter.rs` | OpenRouter API-key provider. Calls `https://openrouter.ai/api/v1/credits`; displays remaining credits. |
| `src/providers/deepgram.rs` | Deepgram API-key provider. Calls projects endpoint, then project balances endpoint; sums USD balances. |
| `src/providers/json_helpers.rs` | Shared JSON traversal helpers: `try_get_property`, `get_string`, `get_decimal`, `get_double`. Tolerates both number and string JSON values. |
| `src/shell/tray.rs` | `TrayIcon`: hidden Win32 message-only window, `Shell_NotifyIconW` tray icon, right-click context menu (Refresh every / High Level / Critical Level submenus + Refresh + Exit), tooltip updates, and Windows notification display. All public methods use `&self` with interior mutability. |
| `src/shell/webview_tooltip.rs` | WebView2-based tooltip popup. Hosts a borderless, top-most, non-activating window with the embedded `usagebar.css` and `tooltip.js`. Receives `TooltipCard` data from Rust via `evaluate_script`, rendered height reported back over wry IPC for auto-sizing. |
| `src/shell/icon.rs` | 32×32 tray icon renderer. CodexBar colour palette (usage-level: green/yellow/orange/red), dark plate, grey track. Bar layout via `build_bar_layout()` with UsageBarRust's division logic (5 cases for Codex/Claude combinations). |
| `src/shell/native.rs` | Raw Win32 FFI declarations (`extern "system"`) for user32, kernel32, gdi32, shell32, ole32. Handle wrappers (`Hwnd`, `Hicon`, `Hmenu`, `Hinstance`) with manual `Send + Sync` impls. DPI helpers. |
| `assets/usagebar.css` | Tooltip-only subset of the Win-CodexBar tray panel styles, limited to the DOM built by `tooltip.js`. |
| `assets/tooltip.js` | DOM builder that constructs the `menu-surface--tray` / `menu-card` / `menu-metric` class hierarchy matching `MenuCard.tsx` and `TrayPanel.tsx`. Communicates with Rust via `window.ipc.postMessage`. |

## Setup Instructions

1. Use Windows for building, running, and manually validating the tray application. The project uses Win32 tray icon APIs and WebView2 and is not cross-platform.
2. Install the latest stable Rust toolchain (`rustup` recommended). The MSVC ABI (`stable-x86_64-pc-windows-msvc`) is required for Win32 FFI.
3. WebView2 Runtime is required on Windows 10; Windows 11 ships it by default. Install from [Microsoft](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) if needed.
4. Build:
```bash
cargo build --release
```

## Development Commands

| Task | Command | Notes |
| ---- | ------- | ----- |
| Build (debug) | `cargo build` | Debug build with console window. |
| Build (release) | `cargo build --release` | Optimised: `opt-level=z`, LTO, stripped. No console window. |
| Run (debug) | `cargo run` | Builds and launches the tray app. |
| Check (no emit) | `cargo check` | Fast compile-check without producing a binary. |
| Clippy | `cargo clippy -- -D warnings` | Linter. |
| Format | `cargo fmt --check` | Verify formatting. |
| Tests | `cargo test` | Runs unit tests in `tooltip.rs` and `icon.rs`. |
| Update deps | `cargo update` | Update `Cargo.lock`. |

## Testing Guidelines

- Unit tests exist in `src/application/tooltip.rs` (7 tests: card building, label mapping, reset hint extraction) and `src/shell/icon.rs` (5 tests: level thresholds, bar layout cases, render success).
- When adding tests, use `#[cfg(test)]` modules within the relevant source files.
- `cargo check` and `cargo clippy -- -D warnings` should also pass before finishing.
- Manual validation should cover:
  - Tray icon appears; hover shows the CodexBar-styled WebView2 tooltip.
  - Usage climbs above `highPercentage` / `criticalPercentage`: Windows notification is shown.
  - Notification repeats only after usage drops below the high threshold and crosses again.
  - Right-click menu submenus (Refresh every, High Level, Critical Level) show the current value checked.
  - Selecting a submenu item writes to `settings.json` and triggers a refresh.
  - Missing credentials or auth files omit affected providers silently.
  - `useLegacyTooltip: true` falls back to the native 127-char tooltip.
  - Tray icon uses CodexBar discrete level colours (green/yellow/orange/red), not the old gradient.

## Code Style and Conventions

- Follow standard Rust idioms and `rustfmt` defaults.
- Prefer `anyhow::Result<T>` for fallible functions; use `anyhow::bail!` for early returns.
- Keep structs and functions module-private unless they are part of a module's public API.
- Use `#[allow(dead_code)]` sparingly.
- Unsafe code is confined to `shell/native.rs` (FFI declarations) and `shell/tray.rs` (calling FFI functions). Do not leak `unsafe` into application or provider code.
- Handle wrappers in `native.rs` (`Hwnd`, `Hicon`, etc.) are `#[repr(transparent)]` with manual `unsafe impl Send + Sync`.
- Provider errors must never crash the app. Return `Ok(None)` when credentials are missing; return `Err` on API/parse failures (the aggregator logs and swallows them).
- Commit messages follow: lowercase, imperative mood, brief summary.

## Architecture Notes

### Application Lifecycle

- `main()` sets **per-monitor DPI awareness** (`DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2`) for crisp WebView2 text, builds a **multi-threaded** Tokio runtime, creates `UsageBarRustHost::create_default()`, and calls `host.run()`.
- `UsageBarRustHost::create_default` wires together `AppLogger`, `SettingsService`, a shared `reqwest::Client` (20-second timeout), and five provider instances.
- `host.run()` reads `useLegacyTooltip` from settings, creates the `TrayIcon` (and the WebView2 popup when not legacy), spawns `RefreshCoordinator::run()` on Tokio, then blocks the main thread on the Win32 message pump.
- Fatal startup exceptions are caught in `main()` and written through `FatalErrorLogger::log`.
- On shutdown, `coord_handle.abort()` stops the refresh loop; `rt.shutdown_timeout(Duration::from_secs(2))` drains remaining tasks.

### Refresh Flow

- `RefreshCoordinator::run` performs an initial refresh, then loops on `tokio::select!` between a periodic sleep and tray events (`Refresh` / `Exit`).
- The refresh period is read from settings every cycle (`refreshPeriodMinute`, clamped to ≥ 1 minute).
- `UsageAggregator::refresh_async` queries all providers concurrently via `futures::future::join_all` with a 45-second aggregate `CancellationToken` timeout.
- Individual provider failures return `None`; they do not crash the app or affect other providers.
- Provider plan names are collected per-provider and forwarded to the tooltip cards.

### Notification System

- `check_threshold_crossings` compares each usage window against the previous snapshot.
- When usage climbs **above** `highPercentage` (default 70): "approaching limit" notification.
- When usage climbs **above** `criticalPercentage` (default 90): "critically high!" notification.
- Notifications are title "Usage Bar", body format: `"{Provider} {Session|Weekly} at N% — approaching limit"` / `"critically high!"`.
- Per-window tracking: `0` = none, `1` = high warned, `2` = critical warned.
- State resets when usage drops below the high threshold, re-enabling notifications.
- Thresholds are configurable via the right-click context menu submenus or `settings.json`.

### Tooltip

Two modes, controlled by `useLegacyTooltip`:
- **`false` (default):** WebView2 popup (`shell/webview_tooltip.rs`). Renders the tooltip-only CodexBar styles in `assets/usagebar.css` with DOM built by `assets/tooltip.js`. Cards group usage windows by provider with `Session`/`Weekly` metric rows. Balance-only providers render compactly with the value right-aligned. No character limit.
- **`true`:** Native Win32 `NOTIFYICONDATA.szTip`, 127-char limit (`tooltip::format`).

### Tray Icon

- Port of CodexBar's `rust/src/tray/render.rs` + `icon.rs`.
- Dark plate background `(60,60,70)`, grey track `(80,80,90)`.
- **Discrete level colours** (not continuous gradient): `<50%` green `(76,175,80)`, `<80%` yellow `(255,193,7)`, `<95%` orange `(255,152,0)`, `≥95%` red `(244,67,54)`.
- Bar division logic (UsageBarRust original):

| Case | Providers present            | Layout      |
|------|------------------------------|-------------|
| 1    | Codex free (7d only)         | full bar    |
| 2    | Codex pro (5h+7d)            | 50-50       |
| 3    | Claude only (5h+7d)          | 50-50       |
| 4    | Codex free + Claude sub      | 50-25-25    |
| 5    | Codex pro + Claude sub       | 25-25-25-25 |

### Context Menu

Right-click menu (built with Win32 `CreatePopupMenu` / `AppendMenuW`):
- **Refresh every** → 1 / 5 / 15 / 60 min (current value ✓-checked)
- **High Level** → 10–90% (current value ✓-checked)
- **Critical Level** → 10–90% (current value ✓-checked)
- ──────── (separator)
- Refresh
- Exit

Selecting a submenu item writes to `settings.json` synchronously and triggers a refresh.

### Provider Pattern

Providers implement `IUsageProvider`:

```rust
#[async_trait]
pub trait IUsageProvider: Send + Sync {
    fn name(&self) -> &str;
    async fn get_usage(
        &self,
        credentials: &ProviderCredentials,
        cancellation_token: tokio_util::sync::CancellationToken,
    ) -> anyhow::Result<Option<ProviderResult>>;
}
```

Provider rules:
- Check credentials or auth material on every refresh.
- Return `Ok(None)` when credentials or auth files are missing.
- Return `Err` on API failures; the aggregator logs and swallows provider failures.
- Return `ProviderResult` with `blocks`, `windows`, and optional `plan` (e.g. "Pro", "Max").
- Respect the provided `CancellationToken` via `tokio::select!`.

Current providers:

| Provider | Credential source | Plan source | API behavior |
| -------- | ----------------- | ----------- | ------------ |
| Codex | `%USERPROFILE%\.codex\auth.json` | `plan_type` in usage response | Calls `https://chatgpt.com/backend-api/wham/usage`; returns 5h and 7d usage windows. |
| Claude | `%USERPROFILE%\.claude\.credentials.json` | `subscriptionType` / `rateLimitTier` | Calls `https://api.anthropic.com/api/oauth/usage`; returns `five_hour` and `seven_day` windows. |
| DeepSeek | `DEEPSEEK_API_KEY` | — | Calls `https://api.deepseek.com/user/balance`; displays USD total balance. |
| OpenRouter | `OPENROUTER_API_KEY` | — | Calls `https://openrouter.ai/api/v1/credits`; displays remaining credits. |
| Deepgram | `DEEPGRAM_API_KEY` | — | Calls projects endpoint, then balances endpoint; sums USD balances. |

## Environment Variables

| Variable | Required | Description |
| -------- | -------- | ----------- |
| `DEEPSEEK_API_KEY` | Optional | Enables the DeepSeek provider. |
| `OPENROUTER_API_KEY` | Optional | Enables the OpenRouter provider. |
| `DEEPGRAM_API_KEY` | Optional | Enables the Deepgram provider. |

Settings are resolved from `%APPDATA%\UsageBarRust\settings.json` first. If blank, falls back to the environment variable.

Codex reads OAuth data from `%USERPROFILE%\.codex\auth.json`. Claude reads OAuth data from `%USERPROFILE%\.claude\.credentials.json` (`claudeAiOauth` key). Do not log or commit the contents of these files.

Default settings file shape:

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

## Dependencies

| Crate | Purpose |
|-------|---------|
| `tokio` (full) | Multi-threaded async runtime |
| `reqwest` (json) | HTTP client with JSON support |
| `serde` / `serde_json` | JSON serialisation/deserialisation |
| `chrono` (serde) | Date/time parsing, formatting, and arithmetic |
| `rust_decimal` (serde) | Precise decimal arithmetic for currency values |
| `anyhow` | Ergonomic error handling |
| `async-trait` | `async fn` in trait definitions |
| `tokio-util` (rt) | `CancellationToken` for aggregate request timeout |
| `urlencoding` | URL-encode project IDs for Deepgram balance endpoint |
| `uuid` (v4) | Unique window class names |
| `futures` | `future::join_all` for parallel provider fan-out |
| `wry` (Windows only) | WebView2 host for the CodexBar-styled tooltip popup |
| `winreg` (Windows only) | Registry access for startup registration |
| `winres` (build, Windows only) | Embed `.ico` resource in the executable |

## Frontend Guidelines

- The tooltip UI is a WebView2 window rendered with the tooltip-only Win-CodexBar styles in `assets/usagebar.css` and the DOM builder in `assets/tooltip.js`.
- Keep `assets/usagebar.css` limited to selectors and custom properties used by `assets/tooltip.js`. When syncing upstream design changes, copy only the relevant tray panel rules and re-test.
- `tooltip.js` builds DOM with the same class hierarchy as `MenuCard.tsx` / `TrayPanel.tsx`: `menu-surface--tray` > `menu-stack` > `menu-stack__item` > `menu-card` > `menu-card__header`, `menu-card__divider`, `menu-metric`, etc.
- Tooltip styling, including compact balance cards and inline plan labels, lives in `assets/usagebar.css`.
- IPC between Rust and JS: JS reports `{type: "ready"}` / `{type: "height", value}`; Rust pushes render data via `window.__render({cards})`.
- Do not add React, npm, or a build step to the shell; keep the JS/CSS asset files self-contained.

## Safety and Security Rules

- Do not commit secrets or local auth files.
- Do not print or log API keys, access tokens, account IDs, or full sensitive API responses.
- Keep credential precedence as implemented: non-blank settings value first, then environment variable fallback.
- Be careful with registry changes. Startup registration uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Unsafe code is confined to `shell/native.rs` and `shell/tray.rs`. Do not introduce new `unsafe` blocks elsewhere.
- Do not introduce network calls from tray hover or other UI-only interactions.

## Agent Workflow

- Inspect relevant source files before editing.
- Follow the existing module boundaries: `domain`, `application`, `infrastructure`, `providers`, `shell`.
- Do not add UI frameworks, dependency injection frameworks, or external packages unless the task explicitly requires them.
- Preserve refresh semantics: providers run in parallel with a shared timeout, provider failures are isolated.
- When adding a new provider:
  1. Create `src/providers/<name>.rs`
  2. Implement `IUsageProvider`
  3. Add `pub mod <name>;` to `src/providers/mod.rs`
  4. Register in `UsageBarRustHost::create_default()` in `host.rs`
  5. If API-key-based, add fields to `AppSettings` and `ProviderCredentials`
- When adjusting the WebView2 tooltip layout, update `assets/usagebar.css` and keep it aligned with the DOM classes emitted by `assets/tooltip.js`.
- Run `cargo check` and `cargo test` before finishing.
- Avoid destructive git or filesystem operations.

## Known Gaps or TODOs

- No CI/CD pipeline is configured.
- No installer is configured.
- Deepgram and OpenRouter providers do not produce `UsageBarWindow` values — they only contribute tooltip text, not icon bars.
- The `plan` field is only populated by Codex (from `plan_type`) and Claude (from `subscriptionType`/`rateLimitTier`); balance providers always return `None`.
- No toast notification is sent when usage drops back below a threshold (tracking state resets silently).

## Maintenance Notes

Update this file when:
- Rust edition, MSRV, or dependency versions change significantly.
- Build, test, format, or release commands change.
- New providers, settings, environment variables, or credential sources are added.
- Automated tests or CI/CD are introduced.
- Architecture boundaries or tray/refresh/notification behavior changes.
- The WebView2 tooltip or CodexBar CSS sync process changes.
