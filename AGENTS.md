# AGENTS.md

## Purpose

This file provides repository-specific instructions for AI coding agents and human contributors working on UsageBar. Keep changes focused, verify assumptions against the repository, and update this document when commands, architecture, dependencies, or conventions change.

## Repository Overview

UsageBar is a small Windows notification-area application for showing LLM/API usage and balance information.

- Language/runtime: C# on `.NET 10`.
- Target framework: `net10.0-windows10.0.17763.0`.
- Application type: Windows executable (`WinExe`).
- UI model: raw Win32 tray icon and hidden message window through P/Invoke in `Shell/Tray`; the custom tooltip uses a WebView2 popup (via `Microsoft.Web.WebView2` NuGet, Core hosting model — no WinForms/WPF wrapper). Legacy `szTip` fallback is preserved when WebView2 runtime is unavailable.
- Providers: Codex OAuth, Claude OAuth, DeepSeek API, OpenRouter API, and Deepgram API.
- Configuration path: `%APPDATA%\UsageBar\settings.json`.
- Log path: `%APPDATA%\UsageBar\app.log`.
- Packaging: Inno Setup script in `installer/UsageBar.iss`.
- CI/CD: GitHub Actions workflow publishes a Windows x64 setup executable for tags matching `v*`.

## Project Structure

```text
.
|-- .editorconfig
|-- .github/workflows/release-desktop.yml
|-- Directory.Build.props
|-- README.md
|-- images/
|   `-- interface.png
|-- installer/
|   `-- UsageBar.iss
`-- src/
    |-- UsageBar.slnx
    `-- UsageBar/
        |-- UsageBar.csproj
        |-- Program.cs
        |-- app.manifest
        |-- Application/
        |-- Domain/
        |-- Infrastructure/
        |-- Providers/
        |-- Assets/
        |   |-- AppIcon.ico
        |   |-- AppIcon.png
        |   |-- usagebar.css
        |   `-- tooltip.js
        `-- Shell/Tray/
```

Important files and directories:

| Path | Purpose |
| ---- | ------- |
| `src/UsageBar.slnx` | Solution file containing the single `UsageBar` project. |
| `src/UsageBar/UsageBar.csproj` | .NET project configuration. Targets `.NET 10` Windows, enables nullable reference types and implicit usings. References `Microsoft.Web.WebView2` (1.0.2903.40) for the custom tooltip. WPF/WinForms WebView2 assemblies are explicitly removed via an MSBuild target since we only use the Core hosting model. |
| `src/UsageBar/app.manifest` | PerMonitorV2 DPI awareness manifest. Required so the WebView2 popup renders sharply and positions correctly on hi-DPI monitors. |
| `src/UsageBar/Program.cs` | `[STAThread]` entry point. Creates and runs `UsageBarHost`; fatal startup failures are logged. |
| `src/UsageBar/Application/` | Host orchestration, refresh scheduling, aggregation, tooltip formatting, and threshold notifications. |
| `src/UsageBar/Domain/UsageModels.cs` | Provider interface (`IUsageProvider`), usage model records (`ProviderResult`, `UsageBarWindow`, `UsageBlock`, `ProviderCredentials`). `UsageBarWindow` carries provider name, window label, and used percent for icon rendering and threshold notifications. |
| `src/UsageBar/Domain/TooltipCard.cs` | `TooltipCard` and `TooltipMetric` records used to push structured data to the WebView2 tooltip. Serialised as `{"cards":[...]}` with camelCase keys matching the verbatim `tooltip.js` DOM builder. |
| `src/UsageBar/Infrastructure/Configuration/` | `settings.json` creation, normalisation, and credential resolution. Includes sync read/write (`ReadSync`/`WriteSync`) for the context-menu UI thread. |
| `src/UsageBar/Infrastructure/Diagnostics/` | File logging for refresh and startup failures. |
| `src/UsageBar/Infrastructure/FileSystem/` | `%APPDATA%\UsageBar` path definitions. |
| `src/UsageBar/Infrastructure/Startup/` | Current-user Windows startup registration through the registry. |
| `src/UsageBar/Providers/` | Codex, Claude, DeepSeek, OpenRouter, Deepgram provider implementations and JSON helpers. Providers now also extract plan/tier information (`plan_type`, `subscriptionType`) for tooltip display and icon layout. |
| `src/UsageBar/Shell/Tray/` | Win32 tray icon, context menu with settings submenus, hidden window, native interop declarations, icon generation (CodexBar palette), WebView2 tooltip popup, tooltip card builder, and STA SynchronizationContext. |
| `src/UsageBar/Assets/usagebar.css` | Tooltip-specific stylesheet, embedded as a build resource and baked into the WebView2 HTML. Keep selectors aligned with the DOM built by `tooltip.js`. |
| `src/UsageBar/Assets/tooltip.js` | Verbatim DOM builder from Rust; renders `MenuCard`-style cards from `window.__render({cards})`. Kept verbatim via an IPC shim (`window.ipc`) injected by the host. |
| `Directory.Build.props` | Enables latest analyzer level, .NET analyzers, and code style enforcement during builds. |
| `.editorconfig` | Formatting and C# style preferences. |
| `.github/workflows/release-desktop.yml` | Release build and GitHub Release publication workflow. |
| `installer/UsageBar.iss` | Inno Setup installer definition. |

## Setup Instructions

1. Use Windows for running and manually validating the tray application. The project targets Windows 10 version `10.0.17763.0` or newer.
2. Install the `.NET 10` SDK. The GitHub Actions workflow uses `dotnet-version: 10.0.x`.
3. The WebView2 Evergreen Runtime is required for the custom tooltip. It ships with Windows 11 and is auto-installed on most Windows 10 systems. If absent, the app silently falls back to the legacy plain-text tooltip.
4. Restore dependencies:

```powershell
dotnet restore src/UsageBar.slnx
```

The project uses `Microsoft.Web.WebView2` (1.0.2903.40) — the only NuGet dependency.

## Development Commands

| Task | Command | Notes |
| ---- | ------- | ----- |
| Restore | `dotnet restore src/UsageBar.slnx` | Supported by the solution and used in CI. |
| Build | `dotnet build src/UsageBar.slnx` | Verified locally on 2026-06-01: succeeded with 0 warnings and 0 errors. Also runs configured analysers/code-style checks. |
| Run app | `dotnet run --project src/UsageBar/UsageBar.csproj` | Based on `README.md`; not run during documentation update because it launches the tray app. Requires Windows shell/tray support. |
| Publish Windows x64 app | `dotnet publish src/UsageBar/UsageBar.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/publish/win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true` | From `.github/workflows/release-desktop.yml`. |
| Build installer | `iscc.exe installer/UsageBar.iss /DAppVersion="<version>" /DSourceDir="<repo>\\artifacts\\publish\\win-x64" /DOutputDir="<repo>\\artifacts\\installer"` | From CI workflow. Requires Inno Setup 6. Use after publish. |
| Tests | Unknown/TODO | No test projects or test files were found. |
| Lint/typecheck | `dotnet build src/UsageBar.slnx` | No separate lint/typecheck command is configured; analysers are enabled in `Directory.Build.props`. |
| Format | Unknown/TODO | No repository-specific formatting command is configured. Follow `.editorconfig`; use SDK/editor formatting cautiously and review diffs. |

## Testing Guidelines

- No automated test project is present in the repository.
- When adding tests, prefer a conventional .NET test project under `src/` or `tests/` and add it to `src/UsageBar.slnx`.
- Until automated tests exist, run `dotnet build src/UsageBar.slnx` for compile/analyser validation.
- Manual validation should cover:
  - Tray icon appears without a main window.
  - Hovering the icon opens the WebView2 custom tooltip with Codex/Claude metric cards (Session + Weekly bars, plan labels, percent used, reset countdown) and balance-only cards (DeepSeek, OpenRouter, Deepgram).
  - Codex metric card appears above Claude in the tooltip; balance cards sort after metric cards.
  - WebView2 runtime absent → falls back to legacy plain-text `szTip` tooltip silently.
  - A usage decrease after a refresh triggers Windows notifications with title "Usage Bar" (e.g. "Claude Session reset to 88%").
  - Usage climbing above configurable high/critical thresholds triggers one-shot notifications ("…at 72% — approaching limit", "…at 95% — critically high!").
  - Each threshold fires only once per window per episode; a reset clears the notification state.
  - Missing credentials omit affected providers.
  - Tray icon uses CodexBar palette: dark plate, grey track, discrete usage-level colours (green <50%, amber <80%, orange <95%, red ≥95%).
  - Icon layout is plan-aware: Codex Free plan uses only the 7d/Weekly window as a full bar.
  - Right-click menu has submenus: Refresh every (1/5/15/60 min), High Level (10-90%), Critical Level (10-90%). Current value is check-marked.
  - Selecting a menu item writes settings.json synchronously and triggers an immediate refresh.
  - Right-click menu `Refresh` triggers an immediate refresh.
  - Right-click menu `Exit` stops the app and removes the tray icon.
  - Settings changes are picked up on the next refresh.
  - Startup registration failures are logged instead of crashing the app.

## Code Style and Conventions

- Follow `.editorconfig`:
  - UTF-8, CRLF line endings, final newline, trim trailing whitespace.
  - Spaces for indentation.
  - C# files use 4-space indentation.
  - `csproj`, `slnx`, `manifest`, and `props` files use 2-space indentation.
  - Prefer file-scoped namespaces.
  - Prefer `var` where configured.
- `Directory.Build.props` enables:
  - `<AnalysisLevel>latest</AnalysisLevel>`
  - `<EnableNETAnalyzers>true</EnableNETAnalyzers>`
  - `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`
- Nullable reference types are enabled in the project. Preserve nullability annotations and handle missing data explicitly.
- Keep classes `internal` unless there is a clear reason to widen visibility.
- Prefer existing BCL APIs and current local patterns. The only external package dependency is `Microsoft.Web.WebView2`.
- Preserve the small, Windows-specific design. Do not introduce WinForms, WPF, DI frameworks, hosting frameworks, or background service frameworks without explicit project direction.
- For WebView2: use the `.Core` hosting model via `CoreWebView2Environment.CreateAsync` + `CreateCoreWebView2ControllerAsync(HWND)`. Do not use the WinForms/WPF wrappers.

## Architecture Notes

### Application Lifecycle

- `Program.Main` creates `UsageBarHost` and starts the tray message loop.
- `UsageBarHost.CreateDefault` wires together:
  - `AppLogger`
  - `SettingsService`
  - a shared `HttpClient` with a 20-second timeout
  - `WebViewTooltip` (WebView2 popup; init is fire-and-forget)
  - `TrayIcon`
  - `TrayUiSyncContext` (installed as `SynchronizationContext.Current` so WebView2 async continuations marshal to the STA UI thread)
  - `RefreshCoordinator`
  - provider instances
- Fatal startup exceptions are caught in `Program.Main` and written through `FatalErrorLogger`.

### Refresh Flow

- `RefreshCoordinator.Start` sets an initial loading tooltip and starts a background refresh.
- Refreshes read settings every time through `SettingsService.ReadAsync`.
- `UsageAggregator.RefreshAsync` queries providers concurrently and catches/logs provider-level failures.
- Provider failures return no blocks for that provider; they should not crash the app.
- `RefreshCoordinator` uses a `SemaphoreSlim` gate to avoid overlapping refreshes.
- Manual refresh disables the current timer, refreshes immediately, and schedules the next refresh relative to the manual refresh time.
- Scheduled refreshes use a one-shot `Timer`; the next timer is scheduled after each refresh.
- The aggregator collects provider plans into `UsageSnapshot.Plans` alongside blocks and windows.

### Tooltip (WebView2 Custom)

- `WebViewTooltip` creates a borderless, top-most, non-activating popup window (`WS_POPUP`, `WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE`) on the tray message-loop thread.
- WebView2 controller is created with `CoreWebView2Environment.CreateAsync` + `CreateCoreWebView2ControllerAsync(HWND)`.
- An IPC shim (`window.ipc={postMessage:(m)=>window.chrome.webview.postMessage(m)}`) is injected via `AddScriptToExecuteOnDocumentCreatedAsync` **before** `NavigateToString` so the verbatim `tooltip.js` works unchanged.
- Host receives JS messages via `CoreWebView2.WebMessageReceived` → `TryGetWebMessageAsString()`:
  - `{"type":"ready"}` → push current snapshot
  - `{"type":"height","value":h}` → resize popup to content height
- Content is pushed from the background refresh thread via `SetContent(TooltipCard[])` → JSON serialise → `PostMessage(WM_TT_SETDATA)` → popup WndProc (UI thread) → `ExecuteScriptAsync("window.__render({cards})")`.
- Show/hide is driven by tray `NIN_POPUPOPEN` / `NIN_POPUPCLOSE` events (requires `NIM_SETVERSION` → `NOTIFYICON_VERSION_4`). `NIF_SHOWTIP` is dropped in custom mode so the native tooltip never appears.
- The popup is positioned above the tray icon (8px gap) using `Shell_NotifyIconGetRect`, clamped to `SPI_GETWORKAREA`. DPI scaling via `GetDpiForWindow`. Rounded corners via `CreateRoundRectRgn` + `SetWindowRgn`.
- If WebView2 init fails (missing runtime, COM error, directory access), `UseLegacyTooltip` is set and falls back to the existing `szTip` path.
- `TooltipCardBuilder.Build(UsageSnapshot)` groups windows into metric cards (Codex first, then Claude) and non-metric blocks into balance cards (DeepSeek, OpenRouter, Deepgram). Plan labels are shown inline on metric cards.
- Data shape serialised to the WebView: `{"cards":[{"title":"Codex","plan":"Pro","metrics":[{"label":"Session","percent":53.0,"detail":"2h 10m"}],...}]}`.
- The HTML page embeds the tooltip-specific `usagebar.css` and verbatim 4.6KB `tooltip.js` at build time (EmbeddedResource → `NavigateToString`).

### TrayIcon Integration

- `TrayIcon` accepts an optional `WebViewTooltip` and `SettingsService`.
- In custom tooltip mode, the icon is registered with `NIM_SETVERSION` (version 4) so the shell sends `NIN_POPUPOPEN`/`NIN_POPUPCLOSE` events. `NIF_SHOWTIP` is omitted.
- `UpdateCards(IReadOnlyList<TooltipCard>)` pushes content to the WebView2 popup. No-op in legacy mode.
- `ShowNotification(string message)` displays a Windows balloon notification with title "Usage Bar" and no subtitle.
- Context menu includes three submenus (Refresh every, High Level, Critical Level) with current value check-marks. Settings changes are written synchronously via `SettingsService.ReadSync()`/`WriteSync()` and trigger an immediate refresh.

### Threshold Notifications

- `AppSettings` includes `HighPercentage` (default 70) and `CriticalPercentage` (default 90).
- `RefreshCoordinator` maintains a per-window notification level dictionary (`"Provider|Label"` → 0/1/2).
- On each refresh, every window's used percent is compared against the previous snapshot:
  - **Reset** (usage drops): `"Codex Session reset to 88%"`, level cleared
  - **Critical** (climbs above critical threshold, fires once): `"Codex Session at 95% — critically high!"`
  - **High** (climbs above high threshold, fires once): `"Claude Weekly at 72% — approaching limit"`
- Thresholds are percentages (1-100) stored in settings; values are divided by 100 at comparison time.

### TrayIcon and Icon Behavior

- Legacy tooltip text (fallback mode) is built by `TooltipFormatter` from display-ready `UsageBlock` values, limited to 127 characters.
- Tray icon is generated by `IconFactory.CreateUsageIcon(windows, plans)`.
- Icon visual style uses the CodexBar palette:
  - Dark plate background: `(60, 60, 70)`
  - Grey bar track: `(80, 80, 90)`
  - Discrete usage-level fill colours: Green `#4CAF50` (<50%), Amber `#FFC107` (<80%), Orange `#FF9800` (<95%), Red `#F44336` (≥95%)
  - 2px transparent margin around the plate; bars inset at (4,28) horizontally, (6,26) vertically
- Icon layout is plan-aware:
  - Codex Free plan is detected from the plan list; its primary window is Weekly (7d) rather than Session (5h)
  - Layout cases: Codex Pro + Claude subscriber → 25-25-25-25; Codex Free + Claude → 50-25-25; single provider → 50-50 or full bar; fallback → empty bar
  - Separators: 1px within same provider, 2px between different providers
- Window labels are "Session" (5h) and "Weekly" (7d).

### Context Menu Settings

Submenu structure:

```
├─ Refresh every ▸
│  ├─  1 min
│  ├─  5 min ✓
│  ├─ 15 min
│  └─ 60 min
├─ High Level ▸
│  ├─ 10% … 90% ✓(70)
├─ Critical Level ▸
│  ├─ 10% … 90% ✓(90)
├─ ────────── (separator)
├─ Refresh
└─ Exit
```

- Submenu items use `MF_CHECKED` for the current value.
- Command IDs are ranged: 2001-2004 (refresh period), 3001-3009 (high level), 4001-4009 (critical level).
- Selecting an item calls `SettingsService.WriteSync()` and fires `RefreshRequested`.

### Provider Pattern

Providers implement `IUsageProvider`:

```csharp
Task<ProviderResult?> GetUsageAsync(ProviderCredentials credentials, CancellationToken cancellationToken);
```

`ProviderResult` includes an optional `Plan` field (e.g. "Pro", "Free", "Max") extracted from API responses.

Provider rules:

- Check credentials or auth material on every refresh.
- Return `null` when required credentials are missing.
- Throw on API failures, parsing failures, or unexpected response shapes; the aggregator logs provider failures.
- Return display-ready `UsageBlock` values, `UsageBarWindow` values for icon rendering, and an optional plan label for tooltip display.
- Use `ProviderJson` helpers for tolerant JSON number/string parsing and property reads where appropriate.
- Respect the provided `CancellationToken`.

Current providers:

| Provider | Credential source | Plan source | API behavior |
| -------- | ----------------- | ----------- | ------------ |
| Codex | `%USERPROFILE%\.codex\auth.json` with `access_token` and `account_id` | `plan_type` field (maps "free"→"Free", "plus"→"Plus", "pro"→"Pro", etc.) | Calls `https://chatgpt.com/backend-api/wham/usage`; returns Session (5h) and Weekly (7d) usage windows when present. |
| Claude | `%USERPROFILE%\.claude\.credentials.json` under `claudeAiOauth.accessToken` | `subscriptionType` or `rateLimitTier` (maps "max"→"Max", "pro"→"Pro", "free"→"Free", etc.) | Calls `https://api.anthropic.com/api/oauth/usage` with `anthropic-beta: oauth-2025-04-20` header and `claude-code/2.1.0` user agent; returns Session (five_hour) and Weekly (seven_day) windows. |
| DeepSeek | `DEEPSEEK_API_KEY` from settings or environment | N/A (balance-only) | Calls `https://api.deepseek.com/user/balance`; displays USD total balance as a balance card. |
| OpenRouter | `OPENROUTER_API_KEY` from settings or environment | N/A (balance-only) | Calls `https://openrouter.ai/api/v1/credits`; displays remaining credits as a balance card. |
| Deepgram | `DEEPGRAM_API_KEY` from settings or environment | N/A (balance-only) | Calls projects endpoint, then project balances endpoint; displays USD balance total as a balance card. |

## Environment Variables

Settings are resolved from `%APPDATA%\UsageBar\settings.json` first. If a value in the settings file is blank, `SettingsService` falls back to the user/process environment variable with the same name.

| Variable | Required | Description |
| -------- | -------- | ----------- |
| `DEEPSEEK_API_KEY` | Optional | Enables the DeepSeek provider when set in settings or environment. |
| `OPENROUTER_API_KEY` | Optional | Enables the OpenRouter provider when set in settings or environment. |
| `DEEPGRAM_API_KEY` | Optional | Enables the Deepgram provider when set in settings or environment. |

Codex does not use an environment variable in this codebase. It reads OAuth auth data from `%USERPROFILE%\.codex\auth.json`. Do not log or commit the contents of this file.

Claude does not use an environment variable in this codebase. It reads OAuth auth data from `%USERPROFILE%\.claude\.credentials.json` under the `claudeAiOauth` JSON key. Do not log or commit the contents of this file.

Default settings file shape:

```json
{
  "refreshPeriodMinute": 5,
  "highPercentage": 70,
  "criticalPercentage": 90,
  "useLegacyTooltip": false,
  "DEEPSEEK_API_KEY": "",
  "OPENROUTER_API_KEY": "",
  "DEEPGRAM_API_KEY": ""
}
```

## Database and Migrations

No database, ORM, schema, or migration tooling was found.

## API Guidelines

This application consumes third-party APIs; it does not expose an HTTP API.

- Keep provider HTTP calls inside `src/UsageBar/Providers/`.
- Use the shared `HttpClient` passed into providers.
- Set provider-specific authorisation headers per request.
- Call `EnsureSuccessStatusCode` so non-success responses are treated as provider failures and logged by the aggregator.
- Parse responses defensively and throw clear `InvalidOperationException` messages for missing expected fields.
- Never include API keys, access tokens, account IDs, or full sensitive response payloads in logs.

## Frontend Guidelines

The user interface is the Windows tray icon, WebView2 custom tooltip, and context menu.

- Tray UI lives in `src/UsageBar/Shell/Tray/`.
- WebView2 tooltip is rendered in a borderless popup window; its content is HTML built from embedded `usagebar.css` and `tooltip.js`.
- The WebView2 IPC bridge (`window.ipc`) is injected by the host before page load so `tooltip.js` stays verbatim.
- Keep hover behaviour cheap: hovering the tray icon must use cached data and must not call provider APIs.
- Context menu commands: Refresh every (submenu), High Level (submenu), Critical Level (submenu), Refresh, Exit.

## Backend Guidelines

There is no server backend. The closest backend-like areas are refresh orchestration, configuration, and provider integrations.

- Keep orchestration in `Application/`.
- Keep settings, logging, paths, and startup registration in `Infrastructure/`.
- Keep provider API integrations in `Providers/`.
- Keep shared domain contracts in `Domain/`.

## Release and Packaging

- Release workflow trigger: push a tag matching `v*`.
- CI build job runs on `windows-latest`.
- CI sets up `.NET 10.0.x`, restores, publishes a self-contained `win-x64` single-file app, installs Inno Setup, builds the installer, and uploads the setup executable as an artifact.
- CI publish job runs on Ubuntu, downloads artifacts, and publishes a GitHub Release using `softprops/action-gh-release`.
- Installer metadata and output naming are controlled by `installer/UsageBar.iss`.

## Agent Workflow

- Inspect relevant source and configuration files before editing.
- Modify only files required for the task. For this repository, application code changes should usually be small and localised.
- Follow the existing folder boundaries: `Application`, `Domain`, `Infrastructure`, `Providers`, `Assets`, and `Shell/Tray`.
- Do not add UI frameworks, hosting frameworks, or external packages unless the task explicitly requires them and the tradeoff is documented. `Microsoft.Web.WebView2` is the sole approved external dependency and must use the `.Core` hosting model only.
- Preserve refresh semantics: no overlapping refreshes, manual refresh reschedules from manual refresh time, and tooltip content remains cached.
- Preserve provider semantics: missing credentials return `null`; unexpected provider/API issues throw and are logged by the aggregator.
- Preserve WebView2 semantics: init is fire-and-forget; `UseLegacyTooltip` fallback on any init error; `AddScriptToExecuteOnDocumentCreatedAsync` must run BEFORE `NavigateToString`; controller bounds must be synced on every reposition.
- Update or add tests when behaviour changes once a test project exists. Until then, document manual validation performed.
- Run `dotnet build src/UsageBar.slnx` when possible before finishing.
- Report any commands that could not be run and why.
- Avoid destructive git or filesystem operations. Do not remove user changes, generated artifacts, or ignored files unless the task specifically requires cleanup.
- Do not edit generated `bin/`, `obj/`, `.vs/`, or `artifacts/` content.

## Safety and Security Rules

- Do not commit secrets or local auth files.
- Do not print or log API keys, Codex access tokens, account IDs, or full sensitive API responses.
- Keep credential precedence as implemented unless intentionally changing settings behaviour: non-blank settings value first, then environment variable fallback.
- Be careful with registry changes. Startup registration uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Keep exception handling around startup registration and provider refreshes so one failure does not crash the app.
- Do not disable analysers, nullable checks, or code-style enforcement to make a build pass.
- Do not introduce network calls from tray hover or other UI-only interactions.

## Pull Request / Change Checklist

- [ ] Change is focused and does not include unrelated refactors.
- [ ] Code follows existing folder boundaries and C# style.
- [ ] Provider changes preserve missing-credential and error-logging behaviour.
- [ ] WebView2 changes preserve the `AddScriptToExecuteOnDocumentCreatedAsync`-before-`NavigateToString` ordering.
- [ ] Refresh changes preserve non-overlap and scheduling semantics.
- [ ] Tooltip content remains cached and provider hover does not trigger API calls.
- [ ] `dotnet build src/UsageBar.slnx` was run with 0 errors and 0 warnings (the MSB3277 WindowsBase warning is suppressed by the csproj target).
- [ ] Manual tray checks were performed when UI/runtime behaviour changed.
- [ ] Documentation was updated when commands, settings, providers, packaging, or architecture changed.
- [ ] No secrets, tokens, or private local paths beyond documented generic Windows locations were added.

## Known Gaps or TODOs

- No automated tests are present.
- No repository-specific formatting command is configured.
- No `.env.example` or separate configuration example file exists; configuration is documented in `README.md` and implemented by `SettingsService`.
- Provider API response shapes are validated at runtime but not covered by tests.
- The `Microsoft.Web.WebView2` NuGet package pulls WPF/WinForms assemblies that are stripped by a csproj target to avoid the MSB3277 warning. If the package is updated, verify the removal target still works.
- No nested `AGENTS.md` files are currently necessary. If the project grows, useful candidates would be `src/UsageBar/Providers/AGENTS.md` for provider-specific rules and `src/UsageBar/Shell/Tray/AGENTS.md` for Win32/WebView2 interop rules.

## Maintenance Notes

Update this file when:

- Target framework or SDK version changes.
- Build, test, format, package, or release commands change.
- New providers, settings, environment variables, or credential sources are added.
- Automated tests or additional projects are introduced.
- Architecture boundaries or tray/refresh behaviour changes.
- The WebView2 tooltip data shape (`TooltipCard`/`TooltipMetric`) or IPC protocol changes.
