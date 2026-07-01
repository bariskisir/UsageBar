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

- `Domain/` — records/enums only: `UsageWindow`, `ProviderResult` (abstract) with `MetricResult` /
  `BalanceResult`, `IconBar`, `UsageSnapshot`, `TooltipCard`, `NotificationLevel`,
  `ThresholdNotification`, `ProviderException`.
- `Configuration/AppSettings.cs` — settings record + `Default` + `Normalize`.
- `Providers/Abstractions/` — `IUsageProvider`, `ProviderDescriptor`, `BalanceUsageProvider`,
  `ProviderQueryContext`, `CredentialNames`, `ProviderJson`, `ProviderHttp`, `MetricWindows`,
  `UsageFormatting`, `IResultDisplayOrderProvider`, provider-facing auth-reader interfaces.
- `Providers/<Name>/` — one folder per provider (Codex, Claude, Antigravity, ElevenLabs, Kilo, DeepSeek, OpenRouter, Moonshot, Deepgram, OpenAI, Venice, Copilot, Crof, Codebuff, Warp, Zai, Synthetic, Chutes, MiniMax, Poe, Alibaba).
- `Application/` — `UsageRefreshService`, `UsageAggregator`, `ThresholdNotifier`,
  `TooltipCardBuilder`, `IconLayout`, and the `Abstractions/` the shell implements
  (`IUsageView`, `ISettingsStore`, `IClock`) plus internal orchestration seams.
- `Application/Notifications/` - notification implementations and helpers
  (`DiscordNotificationService`, `TelegramNotificationService`, threshold notification dispatch,
  payload records, and source-generated notification JSON context).

### App layout

- `Program.cs` — `[STAThread]` entry; configures Serilog, builds the DI container, runs
  `TrayApplication`, logs fatal exceptions.
- `ServiceConfiguration.cs` — all DI registrations.
- `Tray/` — `TrayApplication` (lifecycle), `TrayIconWindow` (Win32 window/icon/balloon),
  `TrayContextMenu`, `IconRenderer`, `TrayUsageView` (`IUsageView`), `NativeMethods`,
  `TrayUiSyncContext`; tray-specific interfaces live under `Tray/Abstractions/`.
- `Tooltip/WebViewTooltip.cs` — WebView2 popup; tooltip interfaces live under
  `Tooltip/Abstractions/`.
- `Infrastructure/` — `JsonSettingsStore`, `ApplicationPaths`, `StartupRegistrationService`,
  `SystemClock`; infrastructure interfaces live under `Infrastructure/Abstractions/`.
- `Assets/` — `AppIcon.*`, `openai.svg`, `claude.svg`, and `index.html` (the whole tooltip page —
  inline CSS + JS, no separate base/override split, UsageBar-native class names
  `panel` / `stack` / `card` / `metric`; embedded-resource name `UsageBar.Assets.index.html`,
  loaded verbatim by the host). The SVGs are embedded as base64 data URIs into the HTML at load
  time and referenced by the tooltip JS via `providerIcon()`.

## Commands

| Task | Command |
| ---- | ------- |
| Restore | `dotnet restore UsageBar.slnx` |
| Build | `dotnet build UsageBar.slnx` |
| Test | `dotnet test UsageBar.slnx` |
| Run | `dotnet run --project src/UsageBar.App` |
| Publish x64 | `dotnet publish src/UsageBar.App/UsageBar.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:Version=APP_VERSION` |
| Publish ARM64 | `dotnet publish src/UsageBar.App/UsageBar.App.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:Version=APP_VERSION` |

> **Version note:** Replace `APP_VERSION` with the **numeric** tag (e.g. `2.11.0`), *not* `v2.11.0`. The MSBuild `Version` property does not accept a `v` prefix. In GitHub Actions use `${{ github.ref_name }}` with `v` stripped, e.g. `-p:Version=${{ github.ref_name != null && (github.ref_name.StartsWith('v') ? github.ref_name.Substring(1) : github.ref_name) || '' }}`. Without `-p:Version` the assembly defaults to `1.0.0.0`.

`Directory.Build.props` enables nullable, implicit usings, the latest analyzers, code-style
enforcement, and **`TreatWarningsAsErrors`** for production projects. Keep builds warning-clean.
The test project opts out of warnings-as-errors and the heavy analyzers for readability.

The published app is a self-contained single file (`PublishSingleFile` + compression) built
**without ReadyToRun** (smaller download; the one-time JIT cost is invisible for a resident tray
app) and **without trimming** (the WebView2 SDK is not trim-safe). `InvariantGlobalization` is on
(the app formats with `InvariantCulture` everywhere) and referenced packages' documentation files
are not copied. JSON uses System.Text.Json **source generation** (`SettingsJsonContext`,
`TooltipJsonContext`), so (de)serialization is reflection-free and trim/AOT-ready.

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
  `Now` + resolved API keys), and calls `UsageAggregator.RefreshAsync` (providers queried in
  display order, concurrently; per-provider failures logged + isolated).
- It updates `IUsageView` (icon + tooltip cards) and emits threshold notifications, then schedules
  the next refresh. Refreshes never overlap (`SemaphoreSlim` gate); manual refresh disables the
  timer and reschedules from the manual-refresh time. Hover never triggers a provider call.

### Providers

Every provider exposes a `ProviderDescriptor` (`Name`, `DisplayOrder`, `IsEnabled`) and returns
one or more concrete results via `GetUsageResultsAsync` (the default implementation wraps
`GetUsageAsync` to preserve backward compatibility). Providers that can report both usage and
balance (Kilo) override `GetUsageResultsAsync` to return multiple results in a single refresh.

Before each refresh the aggregator calls `IUsageProvider.RefreshEnabled(context)` so every
provider can synchronously check whether credentials exist and set `Descriptor.IsEnabled`
accordingly. Disabled providers are skipped — no task is created for them. The check is:

- **API-key providers** (Kilo, ElevenLabs, DeepSeek, OpenRouter, Moonshot, Deepgram,
  OpenAI, Venice, Copilot, Crof, Codebuff, Warp, Zai, Synthetic, Chutes, MiniMax, Poe, Alibaba):
  `!string.IsNullOrEmpty(context.GetApiKey(CredentialName))`
- **OAuth providers** (Codex, Claude, Antigravity):
  `!string.IsNullOrEmpty(authReader.Read()?.AccessToken)`
- **Test providers** use the default implementation which sets `IsEnabled = true`.

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
  one or more `UsageWindow`s (used percent clamped 0–100, reset countdown), a plan label,
  and legacy `IconBar` data for the existing constructor shape. Tray icon bar count/order/weight is
  decided by `iconLayout` settings from the metric window keys, not by providers. Auth is read
  through an injected `I{Codex,Claude}AuthReader` when OAuth-backed, or through
  `ProviderQueryContext` when API-key-backed; tokens are never logged.

Shared plumbing keeps providers small and uniform: `ProviderHttp.GetJsonAsync` (send + status
check + streaming JSON parse; the caller builds the request and disposes the document),
`ProviderJson` (tolerant value reads), and `MetricWindows` (non-null window collection with the
standard no-windows `ProviderException`, plus legacy equal-weight icon bars).

ElevenLabs calls `GET https://api.elevenlabs.io/v1/user/subscription` with the `xi-api-key`
header from `ELEVENLABS_API_KEY`. It reports a single Session bar using only
`character_count / character_limit * 100` for the usage percentage and notification thresholds;
the raw character counts are not displayed.

DeepSeek shows the USD balance and additionally the CNY balance when CNY is non-zero
(`"$x / ¥y"`); when CNY is zero only USD is shown.

Moonshot calls `GET https://api.moonshot.ai/v1/users/me/balance` with `Authorization: Bearer KEY`
from `MOONSHOT_API_KEY` and shows `data.available_balance` as a USD balance.

Kilo calls the app.kilo.ai tRPC batch endpoint for `user.getCreditBlocks`,
`kiloPass.getState`, and `user.getAutoTopUpPaymentMethod` with `Authorization: Bearer KEY`
from `KILO_API_KEY`. If Kilo Pass subscription data is present it returns a `MetricResult`
with a single `Pass` window ordered after Claude; otherwise it returns a credit-only
`BalanceResult` ordered after Moonshot (Kimi).

Antigravity (Gemini Code Assist) is an OAuth-backed metric provider. It reads the access token
from `%USERPROFILE%\.gemini\oauth_creds.json` via `IAntigravityAuthReader`. On the first
refresh it calls `POST loadCodeAssist` once to cache the `cloudaicompanionProject` and tier
name, and fetches the latest CLI version tag from GitHub Releases to build the User-Agent
header. Subsequent refreshes call `POST retrieveUserQuotaSummary` with the cached project ID.
Each quota group's buckets become usage windows: `usedPercent = (1 - remainingFraction) * 100`,
the group `description` is split on `:` to extract model names, and the label combines model
names with the bucket's `window` type (e.g. "Claude Opus, Claude Sonnet, GPT-OSS (weekly)").
The `resetTime` field provides the reset countdown. No refresh token flow — the access token
is used as-is; if it expires the user must re-authenticate externally.

**New providers (not yet live-tested with real API keys):**

OpenAI calls `GET https://api.openai.com/v1/dashboard/billing/credit_grants` with
`Authorization: Bearer KEY` from `OPENAI_API_KEY` and shows `total_available` as a USD
balance. Uses a legacy/user API key; project keys return 403.

Venice calls `GET https://api.venice.ai/api/v1/billing/balance` with
`Authorization: Bearer KEY` from `VENICE_API_KEY`. Prefers USD balance when
`consumptionCurrency` is "USD"; falls back to DIEM credits with epoch allocation percentage.

Copilot calls `GET https://api.github.com/copilot_internal/user` with
`Authorization: token KEY` from `COPILOT_API_KEY` (GitHub OAuth token, not Copilot token).
Reports Premium and Chat quota windows with placeholder/unlimited detection.

Crof calls `GET https://crof.ai/usage_api/` with `Authorization: Bearer KEY` from
`CROF_API_KEY` and shows `credits` as a USD balance.

Codebuff calls `POST {baseURL}/api/v1/usage` with body `{"fingerprintId":"codexbar-usage"}`
and `Authorization: Bearer KEY` from `CODEBUFF_API_KEY`. Reports a Quota window with usage
percentage and remaining balance. Default base URL: `https://www.codebuff.com`.

Warp calls `POST https://app.warp.dev/graphql/v2?op=GetRequestLimitInfo` with a GraphQL
query including `$requestContext` variables, `Authorization: Bearer KEY` from `WARP_API_KEY`,
and headers `x-warp-client-id`, `x-warp-os-category`, `User-Agent: Warp/1.0`. Parses
request limit, bonus grants (user + workspace-level), and unlimited status.

Zai calls `GET {baseURL}/api/monitor/usage/quota/limit` with `Authorization: Bearer KEY`
from `ZAI_API_KEY`. Parses limit entries with type/unit labels and percentage or
usage/number calculations. Default base URL: `https://api.z.ai`.

Synthetic calls `GET https://api.synthetic.new/v2/quotas` with `Authorization: Bearer KEY`
from `SYNTHETIC_API_KEY`. Parses quota windows from JSON array or object with `usedPercent`,
`label`, and optional `windowMinutes`/`resetsAt`.

Chutes calls `GET {baseURL}/users/me/subscription_usage` with `Authorization: Bearer KEY`
from `CHUTES_API_KEY`. Reports 4h Rolling and Monthly windows. Default base URL:
`https://api.chutes.ai`.

MiniMax calls `GET https://api.minimax.io/v1/token_plan/remains` (international) or
`https://api.minimaxi.com/v1/token_plan/remains` (China) with `Authorization: Bearer KEY`
from `MINIMAX_API_KEY` and header `MM-API-Source: CodexBar`. Reports model-level usage
windows with remaining percent and points balance.

Poe calls `GET https://api.poe.com/usage/current_balance` with `Authorization: Bearer KEY`
from `POE_API_KEY` and shows `current_point_balance` as a points display.

Alibaba calls `POST https://modelstudio.console.alibabacloud.com/data/api.json?...` with
body `{"queryCodingPlanInstanceInfoRequest":{"commodityCode":"sfm_codingplan_public_intl"}}`
and multiple auth headers (`Authorization: Bearer`, `x-api-key`, `X-DashScope-API-Key`)
from `ALIBABA_API_KEY`. Reports 5h, Weekly, and Monthly quota windows.

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
show. `IconRenderer` (App) rasterizes the laid-out bars to an HICON using the CodexBar palette
(green <50%, amber <80%, orange <95%, red ≥95%), sizing each bar by its weight.

### Tooltip

`WebViewTooltip` is a borderless, top-most, non-activating WebView2 popup shown on
`NIN_POPUPOPEN` / hidden on `NIN_POPUPCLOSE` (the icon is registered as `NOTIFYICON_VERSION_4`
without `NIF_SHOWTIP`). The `window.ipc` shim is injected via
`AddScriptToExecuteOnDocumentCreatedAsync` **before** `NavigateToString` of the single embedded
`Assets/index.html`. Cards are serialised with System.Text.Json source generation
(`TooltipJsonContext`) and pushed from the refresh thread via `PostMessage` → `ExecuteScriptAsync`. If
WebView2 init fails the popup is torn down (`Hwnd == 0`) and the app runs without a hover tooltip
— **there is no legacy text tooltip**.

### Threshold notifications

`ThresholdNotifier` compares each window against the previous refresh. High, critical, and
limit-reached (crossing from below 100% to 100%, shown with the critical icon) each fire once per
window per episode (high/critical defaults 70% / 95%, configurable); a usage drop emits a reset
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
- New providers (OpenAI, Venice, Copilot, Crof, Codebuff, Warp, Zai, Synthetic, Chutes, MiniMax,
  Poe, Alibaba) are implemented but not yet tested with real API keys — endpoint URLs, response
  shapes, and auth mechanisms are documented but unverified against live APIs.
- Tray/WebView2 interop is validated manually (not unit-tested); Core logic is covered by tests.
- Publish is **not trimmed**: the WebView2 SDK emits trim warnings (errors here under
  warnings-as-errors). Revisit if a trim-safe WebView2 ships.
- WebView2 still pulls WPF/WinForms assemblies that are stripped by an MSBuild target in
  `UsageBar.App.csproj`; re-verify that target if the package is upgraded.
