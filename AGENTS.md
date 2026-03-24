# Repository Guidelines

## Project Structure & Module Organization
`Hypersonic/` contains the WinUI desktop app: `Views/`, `ViewModels/`, `Services/`, `Models/`, `Controls/`, `Converters/`, and UI resources under `Assets/`, `Styles/`, `Themes/`, and `Strings/`. `Hypersonic.Client/` is the reusable Subsonic API client with request, response, and entity models. Tests are split by scope: `Hypersonic.Client.UnitTest/` covers the client library, while `Hypersonic.Tests.MSTest/` covers WinUI-specific behavior. Build outputs land in `TestResults/` and standard `bin/` / `obj/` folders.

## Build, Test, and Development Commands
Use the solution file from the repo root.

- `dotnet build Hypersonic.sln -c Debug` builds all projects.
- `dotnet run --project Hypersonic/Hypersonic.csproj` launches the WinUI app locally.
- `dotnet test Hypersonic.Client.UnitTest/Hypersonic.Client.UnitTest.csproj` runs fast library tests.
- `dotnet test Hypersonic.Tests.MSTest/Hypersonic.Tests.MSTest.csproj` runs WinUI MSTest coverage.
- `dotnet test Hypersonic.sln --collect:"XPlat Code Coverage"` runs the full suite with Coverlet output.

## Coding Style & Naming Conventions
Follow `.editorconfig`: 4-space indentation, CRLF line endings, file-scoped namespaces, and Allman braces. Prefer `var` where the type is obvious, keep `System.*` usings first, and avoid `this.` qualification unless needed. Use PascalCase for types, methods, properties, and XAML pages; prefix interfaces with `I`. Match existing suffix patterns such as `*ViewModel`, `*Service`, `*Page`, and `*Request`.

## Testing Guidelines
MSTest is used throughout, with nullable reference types and implicit usings enabled. Name test files after the subject under test, for example `SubsonicClientConstructorTest.cs` or `GetAlbumList2ResponseTest.cs`. Use `[TestMethod]` for library tests and `[UITestMethod]` in `Hypersonic.Tests.MSTest/` when a test must execute on the WinUI UI thread. Keep new tests deterministic and add coverage for both serialization contracts and view-model/service behavior.

## Commit & Pull Request Guidelines
Recent history follows Conventional Commit style with scopes, for example `feat(search): a working search bar` and `docs(ai): update CLAUDE.md`. Keep commits focused and use `feat`, `fix`, `chore`, `docs`, or similar types with a concise scope. PRs should explain the user-visible change, list test coverage, link related issues, and include screenshots or short recordings for UI changes.

## Configuration Tips
Do not commit secrets or real server credentials. Keep local configuration in `Hypersonic/appsettings.json` for development defaults, and verify publish settings under `Hypersonic/Properties/PublishProfiles/` before shipping platform-specific builds.
