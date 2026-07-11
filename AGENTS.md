# UsageBar — Agent Instructions

## Project

UsageBar is a multi-platform desktop tray application that displays LLM/API usage and balance information. It supports many providers (OpenAI, Claude, DeepSeek, Codex, ElevenLabs, etc.) and sends threshold-based notifications via Telegram/Discord.

## Tech Stack

- **.NET 10.0**, C# (`LangVersion: latest`, `Nullable: enable`, `ImplicitUsings: enable`)
- **Solution format**: `.slnx`
- **Core library**: `src/UsageBar.Core` — domain models, providers, configuration, application services
- **Platform apps**: `src/UsageBar.Windows`, `src/UsageBar.MacOS`, `src/UsageBar.Linux`
- **Tests**: `src/UsageBar.Tests` — **xUnit** (`dotnet test`)
- **DI**: `Microsoft.Extensions.DependencyInjection`
- **Logging**: Serilog (`Serilog.Extensions.Logging`)
- **UI (Windows)**: WebView2 (embedded HTML/JS/CSS frontend in `src/UsageBar.Core/Frontend/`)
- **Installer**: Inno Setup (`installer/UsageBar.iss`)

## Build & Run

```bash
# Build
dotnet build

# Run (Windows)
dotnet run --project src/UsageBar.Windows

# Tests
dotnet test

# Publish (single-file, trimmed)
dotnet publish src/UsageBar.Windows -c Release
```

## Code Conventions

- **TreatWarningsAsErrors**: all warnings are errors in production projects (tests are exempt).
- **No XML doc comments required** (`GenerateDocumentationFile: false`).
- Namespaces follow folder structure: `UsageBar.Core.Domain`, `UsageBar.Core.Providers.DeepSeek`, etc.
- Providers live under `src/UsageBar.Core/Providers/<Name>/` with a `<Name>Provider.cs` and optional `.svg` icon.
- Credential readers are in `src/UsageBar.Core/Credentials/`.
- Embedded web resources (HTML, JS, CSS, SVG) are in `.csproj` as `<EmbeddedResource>`.
- Tests use `TestSupport/` folder for stubs, fakes, and helpers.
- Keep provider icons as embedded SVGs — no external icon dependencies.

## Testing

- Framework: **xUnit** (`dotnet test` from repo root)
- Test project: `src/UsageBar.Tests`
- Tests favor readability over analyzer strictness (`TreatWarningsAsErrors: false` in test project).
- Use `FakeHttpMessageHandler` for mocking HTTP calls.
- Use `Stubs` in `TestSupport/` for common test doubles.
