# Repository Guidelines

## Project Overview
Hypersonic is a WinUI 3 desktop music streaming client for Subsonic/OpenSubsonic servers, built with .NET 10 and Windows App SDK 1.8. The app uses MVVM via `CommunityToolkit.Mvvm` and dependency injection via `Microsoft.Extensions.Hosting`.

## Project Structure & Module Organization
`Hypersonic.Core/` is the platform-independent `net10.0` class library containing models, service contracts, helpers (`Json`, `VaultKeyHelper`), and pure services (`PlaybackQueue`, `FileService`, `LoggingService`, logging sinks). `Hypersonic/` is the WinUI app referencing Core: `Activation/`, `Contracts/`, `Helpers/`, `Interop/`, `Views/`, `ViewModels/`, `Services/`, `Models/` (presentation models with `ObservableObject`), `Controls/`, `Converters/`, plus UI resources under `Assets/`, `Strings/`, `Styles/`, and `Themes/`. `Hypersonic.Client/` is the reusable Subsonic API client with endpoint constants plus DTOs under `Models/Entities/`, `Models/Requests/`, and `Models/Responses/`. Tests are split across `Hypersonic.Core.Tests/` (pure logic via ProjectReference to Core), `Hypersonic.Tests/` (WinUI-dependent behavior via ProjectReference to Hypersonic), and `Hypersonic.Client.UnitTest/` (client serialization/contracts). Design notes live in `docs/design/` and `docs/implementation/`.

## Build, Test, and Development Commands
Use the solution file from the repo root.

- `dotnet build Hypersonic.sln -c Debug -p:Platform=x64` builds the full solution.
- `dotnet build Hypersonic.Client/Hypersonic.Client.csproj` builds the reusable client library.
- `dotnet test Hypersonic.Client.UnitTest/Hypersonic.Client.UnitTest.csproj` runs fast client tests.
- `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj` runs pure logic tests (PlaybackQueue, LoggingService, persistence) without WinUI dependencies.
- `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64` runs WinUI-dependent tests.
- `dotnet test Hypersonic.sln --collect:"XPlat Code Coverage" -p:Platform=x64` runs the full suite with coverage.

Use Visual Studio (F5) or MSIX deployment to launch the WinUI app; do not rely on `dotnet run` for `Hypersonic.csproj`.

## Architecture Notes
Register services, pages, and view models in `Hypersonic\App.xaml.cs`. Services are singleton; pages and most view models are transient; `PlayerViewModel` is singleton so playback state survives navigation. When adding a page, wire both DI registration and `PageService.Configure()`. Use `App.GetService<T>()` for resolution, `INavigationAware` for page lifecycle hooks, and the custom `ILoggingService` instead of `Microsoft.Extensions.Logging`.

## Coding Style & Naming Conventions
Follow `.editorconfig`: 4-space indentation, CRLF line endings, file-scoped namespaces, Allman braces, `System.*` usings first, and avoid `this.` unless needed. Projects enable nullable reference types and implicit usings. Use PascalCase for types, methods, properties, and XAML pages; prefix interfaces with `I`. Match existing suffix patterns such as `*ViewModel`, `*Service`, `*Page`, and `*Request`.

## Testing Guidelines
MSTest is used throughout. Use `[TestMethod]` for library and pure-logic tests, and `[UITestMethod]` in `Hypersonic.Tests/` when a test must execute on the WinUI UI thread. Keep tests deterministic and add coverage for both Subsonic serialization contracts and app behavior. `Hypersonic.Core.Tests/` references `Hypersonic.Core` via ProjectReference for proper `net10.0` testing. `Hypersonic.Tests/` references `Hypersonic` via ProjectReference and includes a testhost fix (`OutputType=Exe` + `GenerateProgramFile=true`) for dotnet/sdk#53412.

## Commit & Pull Request Guidelines
Recent history follows Conventional Commit style with scopes, for example `feat(search): a working search bar` and `docs(ai): update CLAUDE.md`. Keep commits focused and use `feat`, `fix`, `chore`, `docs`, or similar types with a concise scope. PRs should explain the user-visible change, list test coverage, link related issues, and include screenshots or short recordings for UI changes.

## Configuration Tips
Do not commit secrets or real server credentials. Keep local defaults in `Hypersonic/appsettings.json`; it is copied to the build output and used when configuring local settings and services. Verify publish settings under `Hypersonic/Properties/PublishProfiles/` before shipping platform-specific builds. There is no central package management or CI config in the repo, so package versions live in each project file.
