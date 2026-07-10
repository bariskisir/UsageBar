# AGENTS.md

Repository-specific guidance for AI agents and contributors working on Usage Bar. Verify claims
against the code and update this file when architecture, commands, providers, or conventions change.

## Overview

Usage Bar is a Windows notification-area app that polls LLM/API providers and shows usage windows
and balances via a generated tray icon and a WebView2 hover tooltip.

- Language/runtime: C# on **.NET 10**.
- Solution: `UsageBar.slnx` (at the repo root) with five projects under `src/`.
- App type: `WinExe` producing **`UsageBar.exe`** (raw Win32 tray via P/Invoke; WebView2 Core
  hosting model — no WinForms/WPF).

## Projects

| Project | TFM | Role |
| ------- | --- | ---- |
| `src/UsageBar.Core` | `net10.0` | Domain, Configuration, Providers, Application logic, portable infrastructure, Serilog logging, embedded web assets (HTML/CSS/JS/SVGs), platform paths, credential readers. The single shared library for all hosts. Packages: `Microsoft.Extensions.Logging.Abstractions`, `Serilog`, `Serilog.Sinks.File`. `AssemblyName` is `UsageBar.Core`; root namespace `UsageBar`. |
| `src/UsageBar.Windows` | `net10.0-windows10.0.17763.0` | Windows-only Win32 tray shell, WebView2 tooltip/settings panels, DI composition root, Windows startup registry. `AssemblyName` is `UsageBar` (so the artifact is `UsageBar.exe`). |
| `src/UsageBar.MacOS` | `net10.0-macos` | macOS NSStatusBar menu-bar icon, NSPopover+WKWebView tooltip, DI composition root, LaunchAgent startup registration. `AssemblyName` is `UsageBar.MacOS`. |
| `src/UsageBar.Linux` | `net10.0` | Linux D-Bus StatusNotifierItem tray icon via `Tmds.DBus`, desktop autostart, DI composition root. `AssemblyName` is `UsageBar.Linux`. |
| `src/UsageBar.Tests` | `net10.0-windows10.0.17763.0` | xUnit tests for Core and Windows internals. |

Dependency direction is **each host → Core**, and **Tests → Windows + Core**. OS host projects
contain only OS-specific shell code (Win32 tray, WebView2, registry); everything else lives in Core.

### Core layout

- `Domain/` — records/enums/interfaces: `UsageWindow`, `ProviderResult` (abstract) with `MetricResult` /
  `BalanceResult`, `UsageSnapshot`, `TooltipCard`, `NotificationLevel`,
  `ThresholdNotification`, `ProviderException`, `ProviderSettings`, `RefreshSettings`,
  `NotificationSettings`, `VisualSettings`, `UpdateSettings`, `TelegramSettings`,
  `DiscordSettings`, `IRemoteNotificationSettings`.
- `Configuration/AppSettings.cs` — settings record with nested objects (`Refresh`, `Notification`,
  `Visual`, `Update`, `Providers`) + `Default` + `Normalize`. The `providers` array holds
  per-provider `apiKey`, `type`, `credential`, and `enabled` flag. Providers with `enabled = false`
  are skipped during refresh.
- `Providers/Abstractions/` — `IUsageProvider`, `ProviderDescriptor`, `BalanceUsageProvider`,
  `ProviderQueryContext`, `CredentialNames`, `ProviderJson`, `ProviderHttp`, `MetricWindows`,
  `UsageFormatting`, `IResultDisplayOrderProvider`, provider-facing auth-reader interfaces.
- `Providers/<Name>/` — one folder per provider (Codex, Claude, Antigravity, ElevenLabs, Kilo, DeepSeek, OpenRouter, Moonshot, Deepgram, OpenAI, Venice, Copilot, Crof, Codebuff, Warp, Zai, Synthetic, Chutes, MiniMax, Poe, Alibaba, ZenMux).
- `Application/` — `UsageRefreshService`, `UsageAggregator`, `ThresholdNotifier`,
  `TooltipCardBuilder`, `IconLayout`, and the `Abstractions/` the shell implements
  (`IUsageView`, `ISettingsStore`, `IClock`, `IProviderQueryContextFactory`) plus internal
  orchestration seams.
- `Application/Notifications/` - notification implementations and helpers
  (`DiscordNotificationService`, `TelegramNotificationService`, threshold notification dispatch,
  payload records, and source-generated notification JSON context).
- `Infrastructure/` — `PlatformPaths` (cross-platform app-data paths), `JsonSettingsStore`
  (file-based settings persistence), `SystemClock`, `SystemProviderQueryContextFactory`,
  `ProviderInitializer`, `UpdateService`, `EmbeddedPageLoader` (combines embedded HTML/CSS/JS
  into self-contained WebView documents), `SettingsJsonContext` (source-gen JSON for settings
  serialization).
- `Infrastructure/Logging/` — `SerilogBootstrap` (cross-host Serilog setup), `LogConfiguration`
  (env-var-driven log levels), `SensitiveDataRedactor` (safe HTTP body snapshots),
  `UsageHttpTelemetryHandler` (structured HttpClient telemetry).
- `Credentials/` — `FileAntigravityAuthReader` (file-based OAuth reader, used as fallback on all platforms),
  `CodexAuthReader` (reads `~/.codex/auth.json`), `ClaudeAuthReader` (reads `~/.claude/.credentials.json`).
  Each host project provides its own `AntigravityAuthReader` that implements `IAntigravityAuthReader`
  using the platform's native credential store (Windows Credential Manager, macOS Keychain, or Linux
  Secret Service/libsecret), with `FileAntigravityAuthReader` (`~/.antigravity/auth.json`) as fallback.
- `Assets/` — provider SVGs, `index.html` / `tooltip.css` / `tooltip.js`, and
  `settings.html` / `settings.css` / `settings.js`. Embedded as resources.

### Windows layout

- `Program.cs` — `[STAThread]` entry; calls `SerilogBootstrap.CreateLogger`, builds the DI container,
  runs `TrayApplication`, logs fatal exceptions.
- `ServiceConfiguration.cs` — all DI registrations (Windows shell + provider wiring).
- `Tray/` — `TrayApplication` (lifecycle), `TrayIconWindow` (Win32 window/icon/balloon),
  `TrayContextMenu`, `IconRenderer`, `TrayUsageView` (`IUsageView`), `NativeMethods`,
  `TrayUiSyncContext`; tray-specific interfaces live under `Tray/Abstractions/`.
- `Tooltip/` — `WebViewTooltip.cs` (WebView2 popup), `WebViewPopupLifecycle`, `TooltipPlacementCalculator`,
  `TooltipJsonContext`; tooltip interfaces under `Tooltip/Abstractions/`.
- `Settings/SettingsPanel.cs` — WebView2 settings panel; `SettingsIpcJsonContext` (IPC message source-gen).
- `Infrastructure/` — `ApplicationPaths` (extends Core `PlatformPaths` with `WebView2DataDirectory`),
  `WebViewEnvironment` (WebView2 init), `StartupRegistrationService` (HKCU registry), `CodexAuthReader`,
  `ClaudeAuthReader`.
- `Infrastructure/Abstractions/IStartupRegistrationService.cs` — Windows-specific startup registration contract.
- `Assets/` — `AppIcon.ico`, `AppIcon.png` (tray icon resources; all other assets are in Core).

### Linux layout

- `Program.cs` — entry; calls `SerilogBootstrap.CreateLogger`, builds the DI container, runs `TrayApplication`.
- `ServiceConfiguration.cs` — all DI registrations (Linux shell + provider wiring).
- `Tray/NativeTray.cs` — D-Bus StatusNotifierItem tray icon, notifications via `org.freedesktop.Notifications`.
- `UsageView.cs` — `IUsageView` adapter delegating to `NativeTray`.
- `TrayApplication.cs` — lifecycle: wires tray events, starts refresh loop, handles Ctrl+C shutdown.
- `Infrastructure/` — `StartupRegistrationService` (autostart `.desktop` file), `AntigravityAuthReader` (libsecret `secret-tool` + file fallback).
- Packages: `Tmds.DBus` (D-Bus communication).

### macOS layout

- `Program.cs` — `NSApplication` entry; calls `NSApplication.Init`, builds DI, runs `TrayApplication`.
- `ServiceConfiguration.cs` — all DI registrations (macOS shell + provider wiring).
- `Tray/NativeTray.cs` — `NSStatusBar` menu-bar icon, dynamic `NSImage` rendering, `NSMenu` context menu.
- `Tooltip/NativeTooltip.cs` — `NSPopover` + `WKWebView` tooltip reusing Core embedded frontend.
- `UsageView.cs` — `IUsageView` adapter delegating to `NativeTray` and `NativeTooltip`.
- `TrayApplication.cs` — lifecycle: wires tray events, starts refresh, runs `NSApplication` event loop.
- `Infrastructure/` — `StartupRegistrationService` (LaunchAgent plist), `AntigravityAuthReader` (Keychain `security` CLI + file fallback).

## Commands

| Task | Command |
| ---- | ------- |
| Restore | `dotnet restore UsageBar.slnx` |
| Build | `dotnet build UsageBar.slnx` |
| Test | `dotnet test UsageBar.slnx` |
| Run Windows | `dotnet run --project src/UsageBar.Windows` |
| Publish Windows x64 | `dotnet publish src/UsageBar.Windows/UsageBar.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:Version=APP_VERSION` |
| Publish Windows ARM64 | `dotnet publish src/UsageBar.Windows/UsageBar.Windows.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:Version=APP_VERSION` |

> **Version note:** Replace `APP_VERSION` with the **numeric** tag (e.g. `2.11.0`), *not* `v2.11.0`. The MSBuild `Version` property does not accept a `v` prefix. In GitHub Actions use `${{ github.ref_name }}` with `v` stripped, e.g. `-p:Version=${{ github.ref_name != null && (github.ref_name.StartsWith('v') ? github.ref_name.Substring(1) : github.ref_name) || '' }}`. Without `-p:Version` the assembly defaults to `1.0.0.0`.

`Directory.Build.props` enables nullable, implicit usings, the latest analyzers, code-style
enforcement, and **`TreatWarningsAsErrors`** for production projects. Keep builds warning-clean.
The test project opts out of warnings-as-errors and the heavy analyzers for readability.

The published app is a self-contained single file (`PublishSingleFile` + compression) built
**without ReadyToRun** (smaller download; the one-time JIT cost is invisible for a resident tray
app) and **without trimming** (the WebView2 SDK is not trim-safe). `InvariantGlobalization` is on
(the app formats with `InvariantCulture` everywhere) and referenced packages' documentation files
are not copied. JSON uses System.Text.Json **source generation** (`SettingsJsonContext`,
`SettingsIpcJsonContext`, `TooltipJsonContext`), so (de)serialization is reflection-free and trim/AOT-ready.

## Conventions

- File-scoped namespaces; 4-space indentation (`.editorconfig`); prefer `var` where configured.
- Public surface of Core is `public`; implementation helpers are `internal` (tests see them via
  `InternalsVisibleTo("UsageBar.Tests")`; the Windows host sees them via `InternalsVisibleTo("UsageBar")`).
- Async with `ConfigureAwait(false)` in Core/library code; respect `CancellationToken`.
- The only hand-written P/Invoke lives in `Tray/NativeMethods.cs`; `SYSLIB1054` is suppressed in
  the App project to keep `DllImport`.
- OS host projects contain only OS-specific code (Win32/WebView2/registry for Windows;
  Linux/macOS scaffolds). Everything else (paths, settings store, logging, auth readers, embedded
  assets, telemetry, clock, update service) lives in Core.

## Architecture notes

### DI + logging

- `ServiceConfiguration.Build` constructs a `ServiceProvider`. We do **not** use the Generic Host
  (`IHost.Run`) because it would conflict with the STA Win32 message loop; `TrayApplication` pumps
  the loop itself.
- Logging is Serilog (file sink at `%APPDATA%\UsageBar\app.log` on Windows, or equivalent platform
  path, size-rolled). `SerilogBootstrap.CreateLogger` in Core configures the logger; each host calls
  it at startup. `ILogger<T>` is consumed everywhere via `Microsoft.Extensions.Logging.Abstractions`.
  The built-in `System.Net.Http.HttpClient` category is overridden to `Warning`;
  `UsageHttpTelemetryHandler` records structured request/response metadata instead. Information
  is the default level; `USAGEBAR_LOG_LEVEL=Debug|Trace` enables detail and
  `USAGEBAR_HTTP_BODY_LOGGING=1` enables bounded, redacted body snapshots.
- Providers receive a configured `HttpClient` from `IHttpClientFactory` (named client `usage`,
  20s timeout). Provider constructors take a plain `HttpClient` to stay trivially unit-testable.

### Refresh flow

- `UsageRefreshService.RunAsync` owns the initial, scheduled and manual refresh loop. It reads
  settings each refresh, asks the App-owned context factory for a `ProviderQueryContext`, and
  calls `UsageAggregator.RefreshAsync` (providers queried in display order, concurrently;
  provider failures and timeout cancellations are isolated).
- Before querying, the aggregator disables any provider whose `enabled` flag is `false` in the
  `providers` array from settings, so disabled providers are never queried.
- It updates `IUsageView` (icon + tooltip cards) and emits threshold notifications, then waits
  for a schedule or manual-refresh channel signal. Refreshes never overlap; manual requests made
  during a refresh are coalesced into the next schedule anchor. Hover never triggers a provider call.

### Providers

Every provider exposes an immutable `ProviderDescriptor` (identity, display/settings order,
authentication metadata, optional icon and manual-layout keys). `QueryAsync` is the aggregator's
primary query contract; the older single/multi-result methods remain compatibility adapters.

Before each refresh the aggregator calls `IUsageProvider.IsConfigured(context)`. Disabled or
unconfigured providers are skipped — no task is created and no mutable enable state is retained.
The check is:

- **API-key providers** (Kilo, ElevenLabs, DeepSeek, OpenRouter, Moonshot, Deepgram,
  OpenAI, Venice, Copilot, Crof, Codebuff, Warp, Zai, Synthetic, Chutes, MiniMax, Poe, Alibaba, ZenMux):
  `!string.IsNullOrEmpty(context.GetApiKey(CredentialName))`
- **OAuth providers** (Codex, Claude, Antigravity):
  `!string.IsNullOrEmpty(authReader.Read()?.AccessToken)`
- **Test providers** use the default implementation which returns `true`.

`IUsageProvider.GetUsageAsync` returns `null` when not configured; it throws on API/parse
failures (the aggregator logs and isolates). Providers whose placement depends on their concrete
result may implement `IResultDisplayOrderProvider`; the aggregator applies that order after
refresh and flattens metric windows from the ordered results.
Two standards:

- **Balance** providers derive from `BalanceUsageProvider`, declare their `Descriptor` and
  `CredentialName`, and implement `FetchBalanceAsync` (return a display-ready string built with
  `UsageFormatting.Currency`, which defaults to the USD sign and accepts a custom symbol). They
  yield a `BalanceResult`.
- **Metric** providers implement `IUsageProvider` directly, returning a `MetricResult` with
  one or more `UsageWindow`s (used percent clamped 0–100, reset countdown) and a plan label. Tray
  icon bar count/order/weight is
  decided by `iconLayout` settings from the metric window keys, not by providers. Auth is read
  through an injected `I{Codex,Claude}AuthReader` when OAuth-backed, or through
  `ProviderQueryContext` when API-key-backed; tokens are never logged.

Shared plumbing keeps providers small and uniform: `ProviderHttp.GetJsonAsync` (send + status
check + streaming JSON parse; the caller builds the request and disposes the document),
`ProviderJson` (tolerant value reads), and `MetricWindows` (non-null window collection with the
standard no-windows `ProviderException`).

[Provider-specific API documentation omitted for brevity — see full AGENTS.md history.]

To add a provider: new folder under `Providers/`, implement the right base/interface (declare a
`Descriptor`; for metric providers, return stable `UsageWindow` labels used for icon-layout keys),
and add one registration in
`ServiceConfiguration.cs`. Nothing else — ordering, tray-icon layout, and tooltip cards are all
driven by the descriptor and result, with no provider names hardcoded anywhere.

### Tray icon

`IconLayout.Compute` (Core, unit-tested) builds tray bars from `MetricResult.Windows`, not from
balance results. `settings.json` controls the layout through `iconLayout`. Auto mode shows
every metric window equally in provider display order. Manual mode shows only configured keys, in
JSON order, using values as bar height percentages (e.g. `codex_session`, `codex_weekly`,
`claude_session`, `claude_weekly`, `elevenlabs_session`, `kilo_pass`, `copilot_premium`,
`copilot_chat`, `warp_requests`, `synthetic_rolling_5h`, `codebuff_quota`). Keys ending with `*`
act as a wildcard prefix (e.g. `minimax_*` matches all MiniMax model windows, `zai_*` matches all
Zai limit windows). Unknown or non-positive manual entries are ignored. In manual mode, configured
values below a total of 100 leave the remaining icon space as an empty bottom bar; there is no
serialized `IsManual` field. The layout falls back to a single empty bar when there is nothing to
show. `IconRenderer` (Windows host) rasterizes the laid-out bars to an HICON using the CodexBar palette
(green <50%, amber <80%, orange <95%, red ≥95%), sizing each bar by its weight.

### Tooltip

`WebViewTooltip` is a borderless, top-most, non-activating WebView2 popup shown on
`NIN_POPUPOPEN` / hidden on `NIN_POPUPCLOSE` (the icon is registered as `NOTIFYICON_VERSION_4`
without `NIF_SHOWTIP`). The `window.ipc` shim is injected via
`AddScriptToExecuteOnDocumentCreatedAsync` **before** `NavigateToString` of the single embedded
`Assets/index.html` (loaded from Core via `EmbeddedPageLoader`). Cards are serialised with System.Text.Json source generation
(`TooltipJsonContext`) and pushed from the refresh thread via a posted UI message →
`PostWebMessageAsJson`; inbound messages are source-generated typed JSON. Tooltip and settings
popups share `WebViewPopupLifecycle` for low-memory suspend/resume and controller disposal. If
WebView2 init fails the popup is torn down (`Hwnd == 0`) and the app runs without a hover tooltip
— **there is no legacy text tooltip**. Provider SVG icons are embedded in Core and mapped as
base64 data URIs by the tooltip JS via `providerIcon()`.

### Threshold notifications

`ThresholdNotifier` compares each window against the previous refresh. High, critical, and
limit-reached (crossing from below 100% to 100%, shown with the critical icon) each fire once per
window per episode (high/critical defaults 70% / 90%, configurable); a usage drop emits a reset
and clears that window's state. Per refresh a window emits only its most severe new milestone. The
service groups messages per severity into one balloon each.

## Safety and security

- Never log API keys, OAuth access tokens, account ids, or full sensitive responses.
- HTTP logs contain safe route templates, query/header names, status, duration and body fingerprints.
  Redacted snapshots never contain scalar secrets, identifiers, balances or quotas.
- Credential precedence: non-blank `settings.json` value first, then the same-named env var.
- Antigravity OAuth credentials are stored in the platform's native credential store
  (Windows Credential Manager with `gemini:antigravity` target, macOS Keychain, Linux libsecret
  via `secret-tool`), with a read-only fallback to `~/.antigravity/auth.json`. The `Save` method
  always writes to the native credential store; the file is never written by either reader.
  Credential cache migration (file → native store) happens automatically on first read.
- Startup registration writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (Windows host);
  failures are logged, never fatal. Keep provider/startup errors non-crashing.
- Don't add UI/hosting frameworks or extra packages without reason. Don't disable analyzers,
  nullable, or code-style enforcement to make a build pass.

## Known gaps / TODO

- Providers are registered explicitly (no assembly scanning) for clarity.
- New providers (OpenAI, Venice, Copilot, Crof, Codebuff, Warp, Zai, Synthetic, Chutes, MiniMax,
  Poe, Alibaba, ZenMux) are implemented but not yet tested with real API keys — endpoint URLs, response
  shapes, and auth mechanisms are documented but unverified against live APIs.
- Tray/WebView2 interop is validated manually (not unit-tested); Core logic is covered by tests.
- Publish is **not trimmed**: the WebView2 SDK emits trim warnings (errors here under
  warnings-as-errors). Revisit if a trim-safe WebView2 ships.
- WebView2 still pulls WPF/WinForms assemblies that are stripped by an MSBuild target in
  `UsageBar.Windows.csproj`; re-verify that target if the package is upgraded.
- macOS and Linux hosts are scaffolded but not yet tested on real machines.

