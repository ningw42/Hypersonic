# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Hypersonic is a WinUI 3 desktop music streaming client for Subsonic/OpenSubsonic-compatible servers (e.g., Navidrome). Built with .NET 8, Windows App SDK 1.4, and MSIX packaging.

## Build & Test Commands

```bash
# Build the full solution
dotnet build Hypersonic.sln

# Build a specific project
dotnet build Hypersonic/Hypersonic.csproj
dotnet build Hypersonic.Client/Hypersonic.Client.csproj

# Run tests
dotnet test Hypersonic.Tests.MSTest/Hypersonic.Tests.MSTest.csproj
dotnet test Hypersonic.Client.UnitTest/Hypersonic.Client.UnitTest.csproj

# Run a single test
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Build for a specific platform (x64, x86, arm64)
dotnet build Hypersonic.sln -p:Platform=x64
```

Note: The main WinUI app (`Hypersonic.csproj`) targets `net8.0-windows10.0.19041.0` and requires the Windows App SDK. It cannot be run with `dotnet run`; use Visual Studio (F5) or deploy the MSIX package.

## Solution Structure

| Project | Purpose |
|---|---|
| **Hypersonic** | WinUI 3 desktop app (Views, ViewModels, Services, Controls) |
| **Hypersonic.Core** | Shared library (FileService, core contracts) |
| **Hypersonic.Client** | Subsonic/OpenSubsonic API client library |
| **Hypersonic.Tests.MSTest** | App unit tests (MSTest) |
| **Hypersonic.Client.UnitTest** | Client library tests |

## Architecture

**MVVM with Dependency Injection** — Uses CommunityToolkit.Mvvm source generators and Microsoft.Extensions.Hosting.

### Key patterns

- **DI registration**: All services, ViewModels, and Views are registered in `App.xaml.cs`. Services are singleton; Views/ViewModels are transient.
- **Service resolution**: `App.GetService<T>()` is the static accessor for resolving services anywhere.
- **Navigation**: Frame-based. `PageService` maps ViewModel types to Page types by full name. When adding a new page, you must: (1) create the View and ViewModel, (2) register both in `App.xaml.cs`, (3) add the mapping in `PageService.Configure()`.
- **ViewModel base**: ViewModels extend `ObservableRecipient` (from CommunityToolkit.Mvvm). Use `[ObservableProperty]` and `[RelayCommand]` source generator attributes.
- **INavigationAware**: ViewModels implement this contract to receive `OnNavigatedTo`/`OnNavigatedFrom` lifecycle callbacks.
- **Localization**: String resources are in `Strings/en-us/Resources.resw`.

### Subsonic API Client (`Hypersonic.Client`)

- `ISubsonicClient` / `SubsonicClient` — comprehensive wrapper covering System, Browsing, Album/Song Lists, Searching, Playlists, Media Retrieval, Media Annotation, Sharing, Podcasts, User Management, Bookmarks, and Library Scanning APIs.
- API endpoints are defined as constants in `Constants.Action`.
- Request/Response pattern with strongly-typed DTOs in `Models/Entities/`, `Models/Requests/`, `Models/Responses/`.
- Supports both XML and JSON response formats.

## Code Style

Enforced via `.editorconfig`:
- **Indentation**: 4 spaces, Allman brace style (braces on new lines)
- **Naming**: PascalCase for types/members, interfaces prefixed with `I`
- **Preferences**: `var` everywhere, file-scoped namespaces, prefer braces, pattern matching over `as`+null check
- **Line endings**: CRLF
