# AGENTS.md

## Purpose

This file provides repository-specific instructions for AI coding agents and human contributors working on UsageBarRust. Keep changes focused, verify assumptions against the repository, and update this document when commands, architecture, dependencies, or conventions change.

## Repository Overview

UsageBarRust is a minimal Windows notification-area (system tray) application that displays LLM/API usage and balance information as a coloured 32×32 icon. It is a Rust port of the C# [UsageBar](https://github.com/bariskisir/UsageBar) project.

- Language/runtime: Rust (edition 2021) on Tokio async runtime.
- Target platform: Windows only (`#![windows_subsystem = "windows"]`, Win32 tray icon via raw FFI).
- Application type: Windows GUI executable (no console window in release builds).
- UI model: raw Win32 tray icon and hidden message window through `extern "system"` FFI in `shell/native.rs`; no WinForms, WPF, or other UI frameworks.
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
|   `-- AppIcon.ico
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
        `-- tray.rs
```

Important files and directories:

| Path | Purpose |
| ---- | ------- |
| `Cargo.toml` | Crate manifest. Defines the `UsageBarRust` binary (v1.2.0), dependencies, and release profile (`opt-level=z`, LTO, single codegen unit, stripped). |
| `build.rs` | Embeds the application icon (`assets/AppIcon.ico`) via `winres` on Windows builds. |
| `src/main.rs` | Entry point. Builds a multi-threaded Tokio runtime, calls `rt.enter()`, creates `UsageBarRustHost`, and runs the message loop. Fatal startup failures are written to `app.log` through `FatalErrorLogger`. |
| `src/domain.rs` | `IUsageProvider` trait, `ProviderCredentials`, `ProviderResult`, `UsageBarWindow`, `UsageBlock`. Pure data — no I/O dependencies. |
| `src/application/host.rs` | `UsageBarRustHost`: wires providers, settings, and tray. `RefreshCoordinator`: periodic refresh loop + tray event handling on Tokio worker threads. |
| `src/application/aggregator.rs` | Parallel provider fan-out with a 45-second aggregate timeout via `CancellationToken`. |
| `src/application/tooltip.rs` | Formats `UsageBlock` values into a tooltip string truncated to 127 characters (Win32 `NOTIFYICONDATA.szTip` limit). |
| `src/infrastructure/settings.rs` | `SettingsService` reads/writes `%APPDATA%\UsageBarRust\settings.json`. `AppSettings` normalizes values and resolves credentials (settings first, environment variable fallback). |
| `src/infrastructure/paths.rs` | `%APPDATA%\UsageBarRust` path helpers — `settings_file_path()` and `log_file_path()`. |
| `src/infrastructure/logger.rs` | `AppLogger` (async, semaphore-gated file append) and `FatalErrorLogger` (sync, for startup crashes before async infra is ready). |
| `src/infrastructure/startup.rs` | Registers `UsageBarRust` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` via `winreg`. Best-effort — failures are logged, never crash. |
| `src/providers/claude.rs` | Claude OAuth provider. Reads `%USERPROFILE%\.claude\.credentials.json` (`claudeAiOauth.accessToken`). Calls `https://api.anthropic.com/api/oauth/usage` with `anthropic-beta: oauth-2025-04-20` and `claude-code/2.1.0` user agent. Returns `five_hour` and `seven_day` usage windows. |
| `src/providers/codex.rs` | Codex OAuth provider. Reads `%USERPROFILE%\.codex\auth.json` (access token + account ID). Calls `https://chatgpt.com/backend-api/wham/usage`. Returns 5h and 7d usage windows. |
| `src/providers/deepseek.rs` | DeepSeek API-key provider. Calls `https://api.deepseek.com/user/balance`; displays USD total balance. |
| `src/providers/openrouter.rs` | OpenRouter API-key provider. Calls `https://openrouter.ai/api/v1/credits`; displays remaining credits (total_credits − total_usage). |
| `src/providers/deepgram.rs` | Deepgram API-key provider. Calls projects endpoint, then project balances endpoint; sums USD balances. |
| `src/providers/json_helpers.rs` | Shared JSON traversal helpers: `try_get_property`, `get_string`, `get_decimal`, `get_double`. Tolerates both number and string JSON values. |
| `src/shell/tray.rs` | `TrayIcon`: hidden Win32 message-only window (`HWND_MESSAGE`), `Shell_NotifyIconW` tray icon, right-click context menu (Refresh / Exit), tooltip updates, and Windows notification display. All public methods use `&self` with interior mutability (`Mutex<Hicon>`) for thread safety. |
| `src/shell/icon.rs` | 32×32 tray icon renderer. Dynamic bar layout via `build_bar_layout()` (5 cases for Codex/Claude combinations). Colour gradient: 0% green → 50% yellow → 100% red. Uses `CreateIcon` to produce raw `HICON`. |
| `src/shell/native.rs` | Raw Win32 FFI declarations (`extern "system"`). Handle wrappers (`Hwnd`, `Hicon`, `Hmenu`, `Hinstance`) with manual `Send + Sync` impls. All `unsafe` is confined to this module and `tray.rs`. |

## Setup Instructions

1. Use Windows for building, running, and manually validating the tray application. The project uses Win32 tray icon APIs and is not cross-platform.
2. Install the latest stable Rust toolchain (`rustup` recommended). The MSVC ABI (`stable-x86_64-pc-windows-msvc`) is required for Win32 FFI.
3. Build:

```bash
cargo build --release
```

No additional system dependencies beyond the Rust toolchain and Windows SDK (included with Visual Studio Build Tools) are needed.

## Development Commands

| Task | Command | Notes |
| ---- | ------- | ----- |
| Build (debug) | `cargo build` | Debug build with console window. Useful for `println!` debugging. |
| Build (release) | `cargo build --release` | Optimised: `opt-level=z`, LTO, single codegen unit, stripped. No console window. |
| Run (debug) | `cargo run` | Builds and launches the tray app. |
| Check (no emit) | `cargo check` | Fast compile-check without producing a binary. |
| Clippy | `cargo clippy -- -D warnings` | Linter. Treat warnings as errors for CI-quality checks. |
| Format | `cargo fmt --check` | Verify formatting. Use `cargo fmt` to auto-fix. |
| Tests | `cargo test` | Currently no test functions exist; `cargo test` will compile but run zero tests. |
| Update deps | `cargo update` | Update `Cargo.lock` to latest compatible dependency versions. |

## Testing Guidelines

- No automated tests are present in the repository.
- When adding tests, prefer Rust's built-in `#[cfg(test)]` modules within the relevant source files or a `tests/` directory at the crate root.
- Until automated tests exist, run `cargo check` and `cargo clippy -- -D warnings` for compile/lint validation.
- Manual validation should cover:
  - Tray icon appears without a console window (release builds) or main window.
  - Tooltip uses cached text; hovering must not call provider APIs.
  - A usage decrease after a refresh triggers Windows notifications for refreshed limit windows (provider-generic; works for Codex and Claude).
  - Missing credentials or auth files omit affected providers silently.
  - Tray icon dynamically shows bars for each provider's usage windows with correct separator widths (1 px same provider, 2 px cross-provider) and colour gradient.
  - Right-click menu `Refresh` triggers an immediate refresh.
  - Right-click menu `Exit` stops the app and removes the tray icon.
  - Settings changes (e.g., `refreshPeriodMinute`) are picked up on the next refresh cycle.
  - Startup registration failures are logged instead of crashing the app.

## Code Style and Conventions

- Follow standard Rust idioms and `rustfmt` defaults (the project currently has no `rustfmt.toml`).
- `.editorconfig` (if present) applies to non-Rust files.
- Prefer `anyhow::Result<T>` for fallible functions; use `anyhow::bail!` for early returns with context.
- Keep structs and functions module-private (`pub(crate)` or bare) unless they are part of a module's public API.
- Use `#[allow(dead_code)]` sparingly — only on trait methods that not all call-sites use (e.g., `IUsageProvider::name`).
- Unsafe code is confined to `shell/native.rs` (FFI declarations) and `shell/tray.rs` (calling FFI functions). Do not leak `unsafe` into application or provider code.
- Handle wrappers in `native.rs` (`Hwnd`, `Hicon`, etc.) are `#[repr(transparent)]` with manual `unsafe impl Send + Sync` — Win32 handles are thread-safe.
- Provider errors must never crash the app. Return `Ok(None)` when credentials are missing; return `Err` on API/parse failures (the aggregator logs and swallows them).
- Commit messages follow: lowercase, imperative mood, brief summary.
- Match the existing comment density and style: module-level `//!` doc comments, section separators (`// ----`), and concise inline comments.

## Architecture Notes

### Application Lifecycle

- `main()` builds a **multi-threaded** Tokio runtime with `enable_all()`, calls `rt.enter()`, creates `UsageBarRustHost::create_default()`, and calls `host.run()`.
- `UsageBarRustHost::create_default` wires together:
  - `AppLogger`
  - `SettingsService`
  - a shared `reqwest::Client` with a 20-second timeout
  - five provider instances (Codex, Claude, DeepSeek, OpenRouter, Deepgram)
- `host.run()` creates the `TrayIcon`, spawns `RefreshCoordinator::run()` on Tokio, then blocks the main thread on the Win32 message pump (`GetMessageW`).
- Fatal startup exceptions are caught in `main()` and written through `FatalErrorLogger::log`.
- On shutdown, `coord_handle.abort()` stops the refresh loop; `rt.shutdown_timeout(Duration::from_secs(2))` drains remaining tasks.

### Refresh Flow

- `RefreshCoordinator::run` performs an initial refresh, then loops on `tokio::select!` between a periodic sleep and tray events (`Refresh` / `Exit`).
- The refresh period is read from settings every cycle (`settings.refresh_period_minute`, clamped to ≥ 1 minute).
- `UsageAggregator::refresh_async` queries all providers concurrently via `futures::future::join_all` with a 45-second aggregate `CancellationToken` timeout.
- Individual provider failures return `None` for that provider; they do not crash the app or affect other providers.
- `RefreshCoordinator::record_windows` compares each provider's usage windows against the previous snapshot (`previous_windows: Mutex<Vec<UsageBarWindow>>`). When any window's `used_percent` drops by ≥ 0.01 (indicating a limit reset), a Windows notification is shown with the provider and window label (e.g., "Claude 5h limit refreshed").

### Tooltip and Icon Behavior

- Tooltip text is built by `tooltip::format` from display-ready `UsageBlock` values.
- Tooltip text is limited to 127 characters to fit the Win32 `NOTIFYICONDATA.szTip` buffer.
- Tray icon is generated by `icon::create_usage_icon` from a `&[UsageBarWindow]` slice.
- `UsageBarWindow` carries `provider_name`, `window_label` (e.g. "5h", "7d"), and `used_percent`.
- Icon layout is dynamic via `build_bar_layout()`. Bars are stacked vertically with separators (1 px within the same provider, 2 px between different providers). Cases handled:

| Case | Providers present            | Layout      |
|------|------------------------------|-------------|
| 1    | Codex free (7d only)         | full bar    |
| 2    | Codex pro (5h+7d)            | 50-50       |
| 3    | Claude only (5h+7d)          | 50-50       |
| 4    | Codex free + Claude sub      | 50-25-25    |
| 5    | Codex pro + Claude sub       | 25-25-25-25 |

- Bar fill colour follows a continuous gradient: 0% green `(0, 255, 0)` → 50% yellow `(255, 255, 0)` → 100% red `(255, 0, 0)`. Gray `(140, 145, 152)` is used when no usage data is available.

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
- Return `Ok(None)` when required credentials or auth files are missing.
- Return `Err` on API failures, parsing failures, or unexpected response shapes; the aggregator logs and swallows provider failures.
- Return display-ready `UsageBlock` values and `UsageBarWindow` values for tooltip and icon rendering.
- Use `json_helpers` for tolerant JSON number/string parsing and property reads.
- Respect the provided `CancellationToken` via `tokio::select!`.

Current providers:

| Provider | Credential source | API behavior |
| -------- | ----------------- | ------------ |
| Codex | `%USERPROFILE%\.codex\auth.json` with `access_token` and `account_id` | Calls `https://chatgpt.com/backend-api/wham/usage`; returns 5-hour and 7-day usage windows when present. |
| Claude | `%USERPROFILE%\.claude\.credentials.json` under `claudeAiOauth.accessToken` | Calls `https://api.anthropic.com/api/oauth/usage` with `anthropic-beta: oauth-2025-04-20` header; returns `five_hour` and `seven_day` windows with `utilization` fraction (0–1, multiplied by 100 for percent) and optional `resets_at` timestamp. |
| DeepSeek | `DEEPSEEK_API_KEY` from settings or environment | Calls `https://api.deepseek.com/user/balance`; displays USD total balance. |
| OpenRouter | `OPENROUTER_API_KEY` from settings or environment | Calls `https://openrouter.ai/api/v1/credits`; displays remaining credits (total_credits − total_usage). |
| Deepgram | `DEEPGRAM_API_KEY` from settings or environment | Calls projects endpoint, then project balances endpoint; sums USD balances. |

## Environment Variables

Settings are resolved from `%APPDATA%\UsageBarRust\settings.json` first. If a value in the settings file is blank, `SettingsService` falls back to the user/process environment variable with the same name.

| Variable | Required | Description |
| -------- | -------- | ----------- |
| `DEEPSEEK_API_KEY` | Optional | Enables the DeepSeek provider when set in settings or environment. |
| `OPENROUTER_API_KEY` | Optional | Enables the OpenRouter provider when set in settings or environment. |
| `DEEPGRAM_API_KEY` | Optional | Enables the Deepgram provider when set in settings or environment. |

Codex does not use an environment variable. It reads OAuth data from `%USERPROFILE%\.codex\auth.json`. Do not log or commit the contents of this file.

Claude does not use an environment variable. It reads OAuth data from `%USERPROFILE%\.claude\.credentials.json` under the `claudeAiOauth` JSON key. Do not log or commit the contents of this file.

Default settings file shape:

```json
{
  "refreshPeriodMinute": 5,
  "DEEPSEEK_API_KEY": "",
  "OPENROUTER_API_KEY": "",
  "DEEPGRAM_API_KEY": ""
}
```

## Database and Migrations

No database, ORM, schema, or migration tooling is used.

## API Guidelines

This application consumes third-party APIs; it does not expose an HTTP API.

- Keep provider HTTP calls inside `src/providers/`.
- Use the shared `reqwest::Client` passed into provider constructors.
- Set provider-specific authorization headers per request.
- Check `response.status().is_success()` — non-success responses are treated as provider failures and logged by the aggregator.
- Parse responses defensively and return clear errors for missing expected fields.
- Never include API keys, access tokens, account IDs, or full sensitive response payloads in logs.
- Use `json_helpers` for tolerant parsing that handles both JSON numbers and strings for numeric fields.

## Frontend Guidelines

There is no web frontend. The user interface is the Windows tray icon and context menu.

- Tray UI lives in `src/shell/`.
- Preserve the hidden message-window model (`HWND_MESSAGE` via `CreateWindowExW` with `Hwnd(-3)`).
- Keep hover behavior cheap: hovering the tray icon must use cached tooltip text and must not call provider APIs.
- Context menu commands are `Refresh` and `Exit`.
- Raw Win32 FFI stays in `shell/native.rs`; `tray.rs` orchestrates; `icon.rs` renders.

## Backend Guidelines

There is no server backend. The closest backend-like areas are refresh orchestration, configuration, and provider integrations.

- Keep orchestration in `application/`.
- Keep settings, logging, paths, and startup registration in `infrastructure/`.
- Keep provider API integrations in `providers/`.
- Keep shared domain contracts in `domain.rs`.

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
| `uuid` (v4) | Unique window class names for the tray window |
| `futures` | `future::join_all` for parallel provider fan-out |
| `winreg` (Windows only) | Registry access for startup registration |
| `winres` (build, Windows only) | Embed `.ico` resource in the executable |

Release profile (`Cargo.toml`):
- `opt-level = "z"` — optimise for size
- `lto = true` — link-time optimisation
- `codegen-units = 1` — single codegen unit for better inlining
- `strip = true` — strip debug symbols

## Release and Packaging

- No CI/CD pipeline is configured yet.
- Release builds are produced locally: `cargo build --release`.
- The release binary is a standalone `.exe` at `target/release/UsageBarRust.exe` — no installer, no runtime dependency beyond the Windows CRT.
- The binary embeds the app icon via `build.rs` using `winres`.

## Agent Workflow

- Inspect relevant source files before editing. `src/` is small and flat enough that full-file reads are cheap.
- Modify only files required for the task. Application code changes should usually be small and localized.
- Follow the existing module boundaries: `domain`, `application`, `infrastructure`, `providers`, `shell`.
- Do not add UI frameworks, dependency injection frameworks, or external packages unless the task explicitly requires them and the tradeoff is documented.
- Preserve refresh semantics: providers run in parallel with a shared timeout, provider failures are isolated, and tooltip hover remains cached.
- Preserve provider semantics: missing credentials return `Ok(None)`; provider/API issues return `Err` and are logged by the aggregator.
- When adding a new provider:
  1. Create `src/providers/<name>.rs`
  2. Implement `IUsageProvider`
  3. Add `pub mod <name>;` to `src/providers/mod.rs`
  4. Register in `UsageBarRustHost::create_default()` in `host.rs`
  5. If API-key-based, add fields to `AppSettings` and `ProviderCredentials`
- Run `cargo check` and `cargo clippy -- -D warnings` before finishing when possible.
- Report any commands that could not be run and why.
- Avoid destructive git or filesystem operations. Do not remove user changes, generated artifacts, or ignored files unless the task specifically requires cleanup.
- Do not edit generated `target/` content.

## Safety and Security Rules

- Do not commit secrets or local auth files.
- Do not print or log API keys, access tokens, account IDs, or full sensitive API responses.
- Keep credential precedence as implemented: non-blank settings value first, then environment variable fallback.
- Be careful with registry changes. Startup registration uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Keep exception handling around startup registration and provider refreshes so one failure does not crash the app.
- Unsafe code is confined to `shell/native.rs` and `shell/tray.rs`. Do not introduce new `unsafe` blocks in application, domain, infrastructure, or provider code.
- Do not introduce network calls from tray hover or other UI-only interactions.

## Pull Request / Change Checklist

- [ ] Change is focused and does not include unrelated refactors.
- [ ] Code follows existing module boundaries and Rust conventions.
- [ ] Provider changes preserve missing-credential (`Ok(None)`) and error-logging behavior.
- [ ] Refresh changes preserve parallel provider isolation and 45-second timeout.
- [ ] Tooltip text remains cached and within 127-character Win32 limit.
- [ ] Icon layout cases in `build_bar_layout()` are preserved or intentionally extended.
- [ ] `cargo check` passes.
- [ ] `cargo clippy -- -D warnings` passes.
- [ ] `cargo fmt --check` passes.
- [ ] Manual tray checks were performed when UI/runtime behavior changed.
- [ ] Documentation was updated when commands, settings, providers, or architecture changed.
- [ ] No secrets, tokens, or private local paths beyond documented generic Windows locations were added.

## Known Gaps or TODOs

- No automated tests are present.
- No CI/CD pipeline is configured.
- No installer is configured (the C# project uses Inno Setup).
- No `.env.example` or separate configuration example file exists; configuration is documented in `README.md` and implemented by `SettingsService`.
- Provider API response shapes are validated at runtime but not covered by tests.
- No nested `AGENTS.md` files are currently necessary. If the project grows, useful candidates would be `src/providers/AGENTS.md` for provider-specific rules and `src/shell/AGENTS.md` for Win32 interop rules.
- Deepgram and OpenRouter providers do not produce `UsageBarWindow` values — they only contribute tooltip text, not icon bars.
- The `shell/icon.rs` `assign_bar_positions` function has an unused variable `total_ratio` worth noting (the last bar fills remaining space, making the ratio normalisation only approximate).

## Maintenance Notes

Update this file when:

- Rust edition, MSRV, or dependency versions change significantly.
- Build, test, format, or release commands change.
- New providers, settings, environment variables, or credential sources are added.
- Automated tests or CI/CD are introduced.
- Architecture boundaries or tray/refresh behavior changes.
- The project gains cross-platform support (currently Windows-only by design).
