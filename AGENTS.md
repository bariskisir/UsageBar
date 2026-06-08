# AGENTS.md

Repository-specific guidance for AI agents and contributors working on Usage Bar. Verify claims
against the code and update this file when architecture, commands, providers, or conventions change.

## Overview

Usage Bar is a Windows notification-area app that polls LLM/API providers and shows usage windows
and balances via a generated tray icon and a WebView2 hover tooltip.

- Language/runtime: C# on **.NET 10**.
- Solution: `UsageBar.slnx` (at the repo root) with three projects under `src/`.
- App type: `WinExe` producing **`UsageBar.exe`** (raw Win32 tray via P/Invoke; WebView2 Core
  hosting model — no WinForms/WPF).

## Projects

| Project | TFM | Role |
| ------- | --- | ---- |
| `src/UsageBar.Core` | `net10.0` | Domain, Configuration, Providers, Application logic. No Windows/WebView2 dependency. Only package: `Microsoft.Extensions.Logging.Abstractions`. `AssemblyName` is `UsageBar.Core`; root namespace `UsageBar`. |
| `src/UsageBar.App` | `net10.0-windows10.0.17763.0` | Win32 tray shell, WebView2 tooltip, Serilog, DI composition root. `AssemblyName` is `UsageBar` (so the artifact is `UsageBar.exe`); root namespace `UsageBar`. |
| `src/UsageBar.Tests` | `net10.0` | xUnit tests against `UsageBar.Core` (internals exposed via `InternalsVisibleTo`). |

Dependency direction is **App → Core ← Tests**. Keep business logic in Core so it stays
testable; keep all Win32/WebView2/registry code in App.

### Core layout

- `Domain/` — records/enums only: `UsageWindow`, `ProviderResult`, `ProviderCategory`,
  `UsageSnapshot`, `ProviderPlan`, `TooltipCard`, `NotificationLevel`, `ThresholdNotification`,
  `ProviderException`.
- `Configuration/AppSettings.cs` — settings record + `Default` + `Normalize`.
- `Providers/Abstractions/` — `IUsageProvider`, `BalanceUsageProvider`, `ProviderQueryContext`,
  `CredentialNames`, `ProviderJson`, `UsageFormatting`.
- `Providers/<Name>/` — one folder per provider (Codex, Claude, DeepSeek, OpenRouter, Deepgram).
- `Application/` — `UsageRefreshService`, `UsageAggregator`, `ThresholdNotifier`,
  `TooltipCardBuilder`, `IconLayout`, and the `Abstractions/` the shell implements
  (`IUsageView`, `ISettingsStore`, `IClock`).

### App layout

- `Program.cs` — `[STAThread]` entry; configures Serilog, builds the DI container, runs
  `TrayApplication`, logs fatal exceptions.
- `ServiceConfiguration.cs` — all DI registrations.
- `Tray/` — `TrayApplication` (lifecycle), `TrayIconWindow` (Win32 window/icon/balloon),
  `TrayContextMenu`, `IconRenderer`, `TrayUsageView` (`IUsageView`), `NativeMethods`,
  `TrayUiSyncContext`.
- `Tooltip/WebViewTooltip.cs` — WebView2 popup.
- `Infrastructure/` — `JsonSettingsStore`, `ApplicationPaths`, `StartupRegistrationService`,
  `SystemClock`.
- `Assets/` — `AppIcon.*`, and the embedded `usagebar.css` / `tooltip.js` (kept verbatim;
  embedded-resource names are `UsageBar.Assets.*`).

## Commands

| Task | Command |
| ---- | ------- |
| Restore | `dotnet restore UsageBar.slnx` |
| Build | `dotnet build UsageBar.slnx` |
| Test | `dotnet test UsageBar.slnx` |
| Run | `dotnet run --project src/UsageBar.App` |
| Publish | `dotnet publish src/UsageBar.App/UsageBar.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true` |

`Directory.Build.props` enables nullable, implicit usings, the latest analyzers, code-style
enforcement, and **`TreatWarningsAsErrors`** for production projects. Keep builds warning-clean.
The test project opts out of warnings-as-errors and the heavy analyzers for readability.

## Conventions

- File-scoped namespaces; 4-space indentation (`.editorconfig`); prefer `var` where configured.
- Public surface of Core is `public`; implementation helpers are `internal` (tests see them via
  `InternalsVisibleTo("UsageBar.Tests")`).
- Async with `ConfigureAwait(false)` in Core/library code; respect `CancellationToken`.
- The only hand-written P/Invoke lives in `Tray/NativeMethods.cs`; `SYSLIB1054` is suppressed in
  the App project to keep `DllImport`.

## Architecture notes

### DI + logging

- `ServiceConfiguration.Build` constructs a `ServiceProvider`. We do **not** use the Generic Host
  (`IHost.Run`) because it would conflict with the STA Win32 message loop; `TrayApplication` pumps
  the loop itself.
- Logging is Serilog (file sink at `%APPDATA%\UsageBar\app.log`, size-rolled) consumed via
  `ILogger<T>`. The built-in `System.Net.Http.HttpClient` category is overridden to `Warning`.
- Providers receive a configured `HttpClient` from `IHttpClientFactory` (named client `usage`,
  20s timeout). Provider constructors take a plain `HttpClient` to stay trivially unit-testable.

### Refresh flow

- `UsageRefreshService` reads settings each refresh, builds a `ProviderQueryContext` (reference
  `Now` + resolved API keys), and calls `UsageAggregator.RefreshAsync` (concurrent, per-provider
  failures logged + isolated).
- It updates `IUsageView` (icon + tooltip cards) and emits threshold notifications, then schedules
  the next refresh. Refreshes never overlap (`SemaphoreSlim` gate); manual refresh disables the
  timer and reschedules from the manual-refresh time. Hover never triggers a provider call.

### Providers

`IUsageProvider` returns `null` when not configured; throws on API/parse failures (the aggregator
logs and isolates). Two standards:

- **Balance** providers derive from `BalanceUsageProvider` and implement `FetchBalanceAsync`
  (return a display-ready string built with `UsageFormatting.Currency`, which defaults to the USD
  sign and accepts a custom symbol) + `Name`/`CredentialName`.
- **Metric** providers implement `IUsageProvider` directly, returning `Session`/`Weekly`
  `UsageWindow`s (used percent clamped 0–100, reset countdown) and a plan label. Auth is read
  through an injected `I{Codex,Claude}AuthReader` (testable; tokens are never logged).

DeepSeek shows the USD balance and additionally the CNY balance when CNY is non-zero
(`"$x / ¥y"`); when CNY is zero only USD is shown.

To add a provider: new folder under `Providers/`, implement the right base/interface, add one
registration in `ServiceConfiguration.cs`.

### Tray icon

`IconLayout.Compute` (Core, unit-tested) decides which windows become bars and in what order
(plan-aware: Codex Free uses the Weekly window; Codex Pro + Claude subscriber → four bars; etc.).
`IconRenderer` (App) rasterizes that to an HICON using the CodexBar palette (green <50%, amber
<80%, orange <95%, red ≥95%).

### Tooltip

`WebViewTooltip` is a borderless, top-most, non-activating WebView2 popup shown on
`NIN_POPUPOPEN` / hidden on `NIN_POPUPCLOSE` (the icon is registered as `NOTIFYICON_VERSION_4`
without `NIF_SHOWTIP`). The `window.ipc` shim is injected via
`AddScriptToExecuteOnDocumentCreatedAsync` **before** `NavigateToString` so `tooltip.js` stays
verbatim. Cards are pushed from the refresh thread via `PostMessage` → `ExecuteScriptAsync`. If
WebView2 init fails the popup is torn down (`Hwnd == 0`) and the app runs without a hover tooltip
— **there is no legacy text tooltip**.

### Threshold notifications

`ThresholdNotifier` compares each window against the previous refresh. High, critical, and
limit-reached (crossing from below 100% to 100%, shown with the critical icon) each fire once per
window per episode (high/critical defaults 70% / 90%, configurable); a usage drop emits a reset
and clears that window's state. Per refresh a window emits only its most severe new milestone. The
service groups messages per severity into one balloon each.

## Safety and security

- Never log API keys, OAuth access tokens, account ids, or full sensitive responses.
- Credential precedence: non-blank `settings.json` value first, then the same-named env var.
- Startup registration writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; failures are
  logged, never fatal. Keep provider/startup errors non-crashing.
- Don't add UI/hosting frameworks or extra packages without reason. Don't disable analyzers,
  nullable, or code-style enforcement to make a build pass.

## Known gaps / TODO

- Providers are registered explicitly (no assembly scanning) for clarity.
- Tray/WebView2 interop is validated manually (not unit-tested); Core logic is covered by tests.
- WebView2 still pulls WPF/WinForms assemblies that are stripped by an MSBuild target in
  `UsageBar.App.csproj`; re-verify that target if the package is upgraded.
