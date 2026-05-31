# AGENTS.md

## Purpose

This file provides repository-specific instructions for AI coding agents and human contributors working on UsageBar. Keep changes focused, verify assumptions against the repository, and update this document when commands, architecture, dependencies, or conventions change.

## Repository Overview

UsageBar is a small Windows notification-area application for showing LLM/API usage and balance information.

- Language/runtime: C# on `.NET 10`.
- Target framework: `net10.0-windows10.0.17763.0`.
- Application type: Windows executable (`WinExe`).
- UI model: raw Win32 tray icon and hidden message window through P/Invoke in `Shell/Tray`; do not add WinForms or WPF unless the project direction changes explicitly.
- Providers: Codex OAuth, DeepSeek API, OpenRouter API, and Deepgram API.
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
        |-- Application/
        |-- Domain/
        |-- Infrastructure/
        |-- Providers/
        `-- Shell/Tray/
```

Important files and directories:

| Path | Purpose |
| ---- | ------- |
| `src/UsageBar.slnx` | Solution file containing the single `UsageBar` project. |
| `src/UsageBar/UsageBar.csproj` | .NET project configuration. Targets `.NET 10` Windows and enables nullable reference types and implicit usings. |
| `src/UsageBar/Program.cs` | `[STAThread]` entry point. Creates and runs `UsageBarHost`; fatal startup failures are logged. |
| `src/UsageBar/Application/` | Host orchestration, refresh scheduling, aggregation, and tooltip formatting. |
| `src/UsageBar/Domain/UsageModels.cs` | Provider interface and usage model records. |
| `src/UsageBar/Infrastructure/Configuration/` | `settings.json` creation, normalization, and credential resolution. |
| `src/UsageBar/Infrastructure/Diagnostics/` | File logging for refresh and startup failures. |
| `src/UsageBar/Infrastructure/FileSystem/` | `%APPDATA%\UsageBar` path definitions. |
| `src/UsageBar/Infrastructure/Startup/` | Current-user Windows startup registration through the registry. |
| `src/UsageBar/Providers/` | Codex, DeepSeek, OpenRouter, Deepgram provider implementations and JSON helpers. |
| `src/UsageBar/Shell/Tray/` | Win32 tray icon, menu, hidden window, native interop declarations, and icon generation. |
| `Directory.Build.props` | Enables latest analyzer level, .NET analyzers, and code style enforcement during builds. |
| `.editorconfig` | Formatting and C# style preferences. |
| `.github/workflows/release-desktop.yml` | Release build and GitHub Release publication workflow. |
| `installer/UsageBar.iss` | Inno Setup installer definition. |

## Setup Instructions

1. Use Windows for running and manually validating the tray application. The project targets Windows 10 version `10.0.17763.0` or newer.
2. Install the `.NET 10` SDK. The GitHub Actions workflow uses `dotnet-version: 10.0.x`.
3. Restore dependencies:

```powershell
dotnet restore src/UsageBar.slnx
```

The project currently has no third-party `PackageReference` entries, but restore is still part of the normal .NET workflow.

## Development Commands

| Task | Command | Notes |
| ---- | ------- | ----- |
| Restore | `dotnet restore src/UsageBar.slnx` | Supported by the solution and used in CI. |
| Build | `dotnet build src/UsageBar.slnx` | Verified locally on 2026-05-31: succeeded with 0 warnings and 0 errors. Also runs configured analyzers/code-style checks. |
| Run app | `dotnet run --project src/UsageBar/UsageBar.csproj` | Based on `README.md`; not run during documentation update because it launches the tray app. Requires Windows shell/tray support. |
| Publish Windows x64 app | `dotnet publish src/UsageBar/UsageBar.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/publish/win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true` | From `.github/workflows/release-desktop.yml`; unverified locally during documentation update. |
| Build installer | `iscc.exe installer/UsageBar.iss /DAppVersion="<version>" /DSourceDir="<repo>\\artifacts\\publish\\win-x64" /DOutputDir="<repo>\\artifacts\\installer"` | From CI workflow. Requires Inno Setup 6. Use after publish. |
| Tests | Unknown/TODO | No test projects or test files were found. |
| Lint/typecheck | `dotnet build src/UsageBar.slnx` | No separate lint/typecheck command is configured; analyzers are enabled in `Directory.Build.props`. |
| Format | Unknown/TODO | No repository-specific formatting command is configured. Follow `.editorconfig`; use SDK/editor formatting cautiously and review diffs. |

## Testing Guidelines

- No automated test project is present in the repository.
- When adding tests, prefer a conventional .NET test project under `src/` or `tests/` and add it to `src/UsageBar.slnx`.
- Until automated tests exist, run `dotnet build src/UsageBar.slnx` for compile/analyzer validation.
- Manual validation should cover:
  - Tray icon appears without a main window.
  - Tooltip uses cached text; hovering must not call provider APIs.
  - Missing credentials omit affected providers.
  - Right-click menu `Refresh` triggers an immediate refresh.
  - Right-click menu `Exit` stops the app and removes the tray icon.
  - Settings changes are picked up on the next refresh.
  - Startup registration failures are logged instead of crashing the app.

## Code Style and Conventions

- Follow `.editorconfig`:
  - UTF-8, CRLF line endings, final newline, trim trailing whitespace.
  - Spaces for indentation.
  - C# files use 4-space indentation.
  - `csproj`, `slnx`, and `props` files use 2-space indentation.
  - Prefer file-scoped namespaces.
  - Prefer `var` where configured.
- `Directory.Build.props` enables:
  - `<AnalysisLevel>latest</AnalysisLevel>`
  - `<EnableNETAnalyzers>true</EnableNETAnalyzers>`
  - `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`
- Nullable reference types are enabled in the project. Preserve nullability annotations and handle missing data explicitly.
- Keep classes `internal` unless there is a clear reason to widen visibility.
- Prefer existing BCL APIs and current local patterns. The project currently has no external package dependencies.
- Preserve the small, Windows-specific design. Do not introduce WinForms, WPF, dependency injection frameworks, hosting frameworks, or background service frameworks without explicit project direction.

## Architecture Notes

### Application Lifecycle

- `Program.Main` creates `UsageBarHost` and starts the tray message loop.
- `UsageBarHost.CreateDefault` wires together:
  - `AppLogger`
  - `SettingsService`
  - a shared `HttpClient` with a 20-second timeout
  - `TrayIcon`
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

### Tooltip and Icon Behavior

- Tooltip text is built by `TooltipFormatter` from display-ready `UsageBlock` values.
- Tooltip text is limited to 127 characters to fit the Win32 notification icon structure.
- Tray icon color and fill are generated in `IconFactory` from Codex 5-hour and 7-day used percentages.
- Only the Codex provider should return `CodexPrimaryUsedPercent` or `CodexSecondaryUsedPercent`, because they drive the tray icon fill/color.

### Provider Pattern

Providers implement `IUsageProvider`:

```csharp
Task<ProviderResult?> GetUsageAsync(ProviderCredentials credentials, CancellationToken cancellationToken);
```

Provider rules:

- Check credentials or auth material on every refresh.
- Return `null` when required credentials are missing.
- Throw on API failures, parsing failures, or unexpected response shapes; the aggregator logs provider failures.
- Return display-ready `UsageBlock` values.
- Use `ProviderJson` helpers for tolerant JSON number/string parsing and property reads where appropriate.
- Respect the provided `CancellationToken`.

Current providers:

| Provider | Credential source | API behavior |
| -------- | ----------------- | ------------ |
| Codex | `%USERPROFILE%\.codex\auth.json` with `access_token` and `account_id` | Calls `https://chatgpt.com/backend-api/wham/usage`; returns 5-hour and 7-day usage windows when present. |
| DeepSeek | `DEEPSEEK_API_KEY` from settings or environment | Calls `https://api.deepseek.com/user/balance`; displays USD total balance. |
| OpenRouter | `OPENROUTER_API_KEY` from settings or environment | Calls `https://openrouter.ai/api/v1/credits`; displays remaining credits. |
| Deepgram | `DEEPGRAM_API_KEY` from settings or environment | Calls projects endpoint, then project balances endpoint; displays USD balance total. |

## Environment Variables

Settings are resolved from `%APPDATA%\UsageBar\settings.json` first. If a value in the settings file is blank, `SettingsService` falls back to the user/process environment variable with the same name.

| Variable | Required | Description |
| -------- | -------- | ----------- |
| `DEEPSEEK_API_KEY` | Optional | Enables the DeepSeek provider when set in settings or environment. |
| `OPENROUTER_API_KEY` | Optional | Enables the OpenRouter provider when set in settings or environment. |
| `DEEPGRAM_API_KEY` | Optional | Enables the Deepgram provider when set in settings or environment. |

Codex does not use an environment variable in this codebase. It reads OAuth auth data from `%USERPROFILE%\.codex\auth.json`. Do not log or commit the contents of this file.

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

No database, ORM, schema, or migration tooling was found.

## API Guidelines

This application consumes third-party APIs; it does not expose an HTTP API.

- Keep provider HTTP calls inside `src/UsageBar/Providers/`.
- Use the shared `HttpClient` passed into providers.
- Set provider-specific authorization headers per request.
- Call `EnsureSuccessStatusCode` so non-success responses are treated as provider failures and logged by the aggregator.
- Parse responses defensively and throw clear `InvalidOperationException` messages for missing expected fields.
- Never include API keys, access tokens, account IDs, or full sensitive response payloads in logs.

## Frontend Guidelines

There is no web frontend. The user interface is the Windows tray icon and context menu.

- Tray UI lives in `src/UsageBar/Shell/Tray/`.
- Preserve the hidden message-window model.
- Keep hover behavior cheap: hovering the tray icon must use cached tooltip text and must not call provider APIs.
- Context menu commands currently are `Refresh` and `Exit`.

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
- Modify only files required for the task. For this repository, application code changes should usually be small and localized.
- Follow the existing folder boundaries: `Application`, `Domain`, `Infrastructure`, `Providers`, and `Shell/Tray`.
- Do not add UI frameworks, hosting frameworks, or external packages unless the task explicitly requires them and the tradeoff is documented.
- Preserve refresh semantics: no overlapping refreshes, manual refresh reschedules from manual refresh time, and tooltip hover remains cached.
- Preserve provider semantics: missing credentials return `null`; unexpected provider/API issues throw and are logged by the aggregator.
- Update or add tests when behavior changes once a test project exists. Until then, document manual validation performed.
- Run `dotnet build src/UsageBar.slnx` when possible before finishing.
- Report any commands that could not be run and why.
- Avoid destructive git or filesystem operations. Do not remove user changes, generated artifacts, or ignored files unless the task specifically requires cleanup.
- Do not edit generated `bin/`, `obj/`, `.vs/`, or `artifacts/` content.

## Safety and Security Rules

- Do not commit secrets or local auth files.
- Do not print or log API keys, Codex access tokens, account IDs, or full sensitive API responses.
- Keep credential precedence as implemented unless intentionally changing settings behavior: non-blank settings value first, then environment variable fallback.
- Be careful with registry changes. Startup registration uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Keep exception handling around startup registration and provider refreshes so one failure does not crash the app.
- Do not disable analyzers, nullable checks, or code-style enforcement to make a build pass.
- Do not introduce network calls from tray hover or other UI-only interactions.

## Pull Request / Change Checklist

- [ ] Change is focused and does not include unrelated refactors.
- [ ] Code follows existing folder boundaries and C# style.
- [ ] Provider changes preserve missing-credential and error-logging behavior.
- [ ] Refresh changes preserve non-overlap and scheduling semantics.
- [ ] Tooltip text remains cached and within Win32 length limits.
- [ ] `dotnet build src/UsageBar.slnx` was run, or the reason it could not be run is documented.
- [ ] Manual tray checks were performed when UI/runtime behavior changed.
- [ ] Documentation was updated when commands, settings, providers, packaging, or architecture changed.
- [ ] No secrets, tokens, or private local paths beyond documented generic Windows locations were added.

## Known Gaps or TODOs

- No automated tests are present.
- No repository-specific formatting command is configured.
- No `.env.example` or separate configuration example file exists; configuration is documented in `README.md` and implemented by `SettingsService`.
- Provider API response shapes are validated at runtime but not covered by tests.
- No nested `AGENTS.md` files are currently necessary. If the project grows, useful candidates would be `src/UsageBar/Providers/AGENTS.md` for provider-specific rules and `src/UsageBar/Shell/Tray/AGENTS.md` for Win32 interop rules.

## Maintenance Notes

Update this file when:

- Target framework or SDK version changes.
- Build, test, format, package, or release commands change.
- New providers, settings, environment variables, or credential sources are added.
- Automated tests or additional projects are introduced.
- Architecture boundaries or tray/refresh behavior changes.
