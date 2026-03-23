# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Hypersonic is a WinUI 3 desktop music streaming client for Subsonic/OpenSubsonic-compatible servers (e.g., Navidrome). Built with .NET 10, Windows App SDK 1.8, and MSIX packaging.

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

Note: The main WinUI app (`Hypersonic.csproj`) targets `net10.0-windows10.0.19041.0` and requires the Windows App SDK. It cannot be run with `dotnet run`; use Visual Studio (F5) or deploy the MSIX package. Always specify `-p:Platform=x64` (or x86/arm64) when building the main app.

## Solution Structure

| Project | Purpose |
|---|---|
| **Hypersonic** | WinUI 3 desktop app (Views, ViewModels, Services, Controls) |
| **Hypersonic.Client** | Subsonic/OpenSubsonic API client library |
| **Hypersonic.Tests.MSTest** | App unit tests (MSTest) |
| **Hypersonic.Client.UnitTest** | Client library tests |

## App Folder Structure

```
Hypersonic/
├── Activation/       # Startup handlers (DefaultActivationHandler)
├── Behaviors/        # XAML Behaviors (NavigationViewHeaderBehavior)
├── Collections/      # Custom collections (IncrementalAlbumCollection)
├── Contracts/
│   ├── Services/     # Service interfaces
│   └── ViewModels/   # ViewModel interfaces (INavigationAware)
├── Controls/         # Custom controls (MarqueeTextBlock, SquarePanel)
├── Converters/       # XAML value converters
├── Helpers/          # Utilities (TitleBarHelper, TrackListTemplateSelector)
├── Interop/          # COM interop (ITaskbarList3 for taskbar thumbnail buttons)
├── Models/           # View-layer models (PlaybackTrack, SongInfo, SearchSuggestionItem)
├── Services/         # Service implementations
├── Strings/en-us/    # Localization resources (Resources.resw)
├── Styles/           # Resource dictionaries (FontSizes, TextBlock, Thickness)
├── Themes/           # Generic.xaml (custom control templates, FavoriteToggleButtonStyle)
├── ViewModels/       # MVVM ViewModels
└── Views/            # XAML Pages
```

## Architecture

**MVVM with Dependency Injection** — Uses CommunityToolkit.Mvvm source generators and Microsoft.Extensions.Hosting.

### Key patterns

- **DI registration**: All services, ViewModels, and Views are registered in `App.xaml.cs`. Services are singleton; Views/ViewModels are transient.
- **Service resolution**: `App.GetService<T>()` is the static accessor for resolving services anywhere.
- **Navigation**: Frame-based. `PageService` maps ViewModel types to Page types by full name. When adding a new page, you must: (1) create the View and ViewModel, (2) register both in `App.xaml.cs`, (3) add the mapping in `PageService.Configure()`.
- **ViewModel base**: ViewModels extend `ObservableRecipient` (from CommunityToolkit.Mvvm). Use `[ObservableProperty]` and `[RelayCommand]` source generator attributes.
- **INavigationAware**: ViewModels implement this contract to receive `OnNavigatedTo`/`OnNavigatedFrom` lifecycle callbacks.
- **Server connection**: `ISubsonicClientService` (singleton) manages the `SubsonicClient` lifecycle. Credentials are persisted via `ILocalSettingsService` under key `"ServerCredential"`. On startup, `DefaultActivationHandler` checks for saved credentials and navigates to `ConnectPage` or `MainPage` accordingly. URL resolution is intelligent: tries `https://<host>/rest` first, then bare host.
- **Localization**: String resources are in `Strings/en-us/Resources.resw`.

### Services

| Service | Responsibility |
|---|---|
| **IPlaybackService** | Audio playback via `MediaPlayer`. Manages queue, now-playing, position, volume, SMTC. Events: `PlaybackStateChanged`, `TrackChanged`, `PositionChanged`. |
| **ISubsonicClientService** | Wraps `SubsonicClient` lifecycle. Credential validation, multi-URL resolution, connection testing. |
| **INavigationService** | Frame-based navigation with `INavigationAware` lifecycle support and back button. |
| **IPageService** | Maps ViewModel type names to Page types via dictionary lookup. |
| **INavigationViewService** | Manages NavigationView menu item selection and header updates. |
| **IThemeSelectorService** | Persists theme preference (Light/Dark/Default) and updates title bar dynamically. |
| **ILocalSettingsService** | Key-value persistence using `ApplicationData` (MSIX) or file-based JSON (unpackaged). |
| **ITaskbarService** | Taskbar thumbnail buttons (Prev/Play-Pause/Next) via `ITaskbarList3` COM interop. Theme-aware icon rendering with GDI+. |
| **IActivationService** | Startup orchestration: credential check → `ConnectPage` or `ShellPage`, theme + taskbar init. |

### Playback architecture

- **Engine**: `Windows.Media.Playback.MediaPlayer` (native Windows media engine).
- **SMTC**: System Media Transport Controls integration for lock screen / media key support. CommandManager is disabled; SMTC managed manually.
- **Queue**: Linear playlist in `PlaybackService` with auto-advance on track end.
- **Position tracking**: `DispatcherQueueTimer` fires every 500ms to update the `Position` property.
- **Event-driven**: PlaybackService raises events; `PlayerViewModel` and `TaskbarService` subscribe to update UI and taskbar state.

### Shell layout

1. **Top bar** — App icon + title (left), search box with `Ctrl+K` hotkey (center), popup with 3-column results (Artists | Albums | Songs).
2. **Sidebar** — `NavigationView` with: Home, Albums, Artists, Playlists, Settings.
3. **Content area** — `Frame` for page content.
4. **Bottom bar** — `PlayerControl` (now playing info, playback buttons, seek bar, volume).

### Subsonic API Client (`Hypersonic.Client`)

- `ISubsonicClient` / `SubsonicClient` — comprehensive wrapper covering System, Browsing, Album/Song Lists, Searching, Playlists, Media Retrieval, Media Annotation, Sharing, Podcasts, User Management, Bookmarks, and Library Scanning APIs.
- API endpoints are defined as constants in `Constants.Action`.
- Request/Response pattern with strongly-typed DTOs in `Models/Entities/`, `Models/Requests/`, `Models/Responses/`.
- Supports both XML and JSON response formats.

## Notable implementation details

- **Incremental loading**: `IncrementalAlbumCollection` implements `ISupportIncrementalLoading` for lazy-loaded album grids (50 items per batch).
- **Search debounce**: 300ms delay via `Task.Delay` + `CancellationTokenSource` to prevent stale API calls.
- **Disc-based grouping**: `AlbumDetailViewModel` groups songs by disc number using a mixed-type collection (`DiscHeaderItem` + `SongInfo`) rendered by `TrackListTemplateSelector`.
- **Taskbar buttons**: `ITaskbarList3` COM interop renders Segoe MDL2 Assets glyphs to bitmaps via GDI+, theme-aware (light/dark).
- **Custom controls**: `MarqueeTextBlock` (Composition API horizontal scroll animation), `SquarePanel` (1:1 aspect ratio layout).
- **Cover art**: Loaded via `SubsonicClient.GetCoverArtUri()`, displayed as `BitmapImage` in XAML and `RandomAccessStreamReference` for SMTC.

## Key dependencies

| Package | Version | Purpose |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.1 | MVVM source generators (`[ObservableProperty]`, `[RelayCommand]`) |
| Microsoft.Extensions.Hosting | 10.0.5 | Dependency injection container |
| Microsoft.WindowsAppSDK | 1.8 | WinUI 3 framework |
| Microsoft.Xaml.Behaviors.WinUI.Managed | 3.0.1 | XAML Behaviors |
| Newtonsoft.Json | 13.0.4 | Settings JSON serialization |
| System.Drawing.Common | 10.0.5 | GDI+ for taskbar icon rendering |
| WinUIEx | 2.9.0 | Extended WinUI utilities (`WindowEx` base class) |

## Testing

- **Framework**: MSTest with `coverlet.collector` for coverage.
- **Client tests**: Deserialization tests for XML/JSON API responses, client constructor validation.
- **App tests**: Server URL resolution logic.
- **No mocking library** — tests are integration-oriented against real serialized data.

## Code Style

Enforced via `.editorconfig`:
- **Indentation**: 4 spaces, Allman brace style (braces on new lines)
- **Naming**: PascalCase for types/members, interfaces prefixed with `I`
- **Preferences**: `var` everywhere, file-scoped namespaces, prefer braces, pattern matching over `as`+null check
- **Line endings**: CRLF
