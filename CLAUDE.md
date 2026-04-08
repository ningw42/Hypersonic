# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Hypersonic is a WinUI 3 desktop music streaming client for Subsonic/OpenSubsonic-compatible servers (e.g., Navidrome). Built with .NET 10, Windows App SDK 1.8, and MSIX packaging.

## Build & Test Commands

```bash
# Build the full solution
dotnet build Hypersonic.sln -p:Platform=x64

# Build a specific project
dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64
dotnet build Hypersonic.Client/Hypersonic.Client.csproj

# Run tests
dotnet test Hypersonic.Tests.MSTest/Hypersonic.Tests.MSTest.csproj
dotnet test Hypersonic.Client.UnitTest/Hypersonic.Client.UnitTest.csproj
dotnet test Hypersonic.UnitTest/Hypersonic.UnitTest.csproj

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
| **Hypersonic.Tests.MSTest** | App unit tests (MSTest, WinUI `[UITestMethod]`) |
| **Hypersonic.Client.UnitTest** | Client library tests |
| **Hypersonic.UnitTest** | Pure logic unit tests (PlaybackQueue, LoggingService) |

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
├── Helpers/          # Utilities (TitleBarHelper, RuntimeHelper, TrackListTemplateSelector)
├── Interop/          # COM interop (ITaskbarList3 for taskbar thumbnail buttons)
├── Models/           # View-layer models and enums (PlaybackTrack, SongInfo, BackdropType, RepeatMode)
├── Services/
│   └── Logging/      # Log sinks (FileSink, DebugSink)
├── Strings/en-us/    # Localization resources (Resources.resw)
├── Styles/           # Resource dictionaries (FontSizes, TextBlock, Thickness)
├── Themes/           # Generic.xaml (custom control templates, converters, FavoriteToggleButtonStyle)
├── ViewModels/       # MVVM ViewModels
└── Views/            # XAML Pages
```

## Architecture

**MVVM with Dependency Injection** — Uses CommunityToolkit.Mvvm source generators and Microsoft.Extensions.Hosting.

### Key patterns

- **DI registration**: All services, ViewModels, and Views are registered in `App.xaml.cs`. Services are singleton; Views/ViewModels are transient. Exceptions: `PlayerViewModel` is **singleton** (persists across navigation); `INavigationViewService` is **transient**.
- **Service resolution**: `App.GetService<T>()` is the static accessor for resolving services anywhere.
- **Navigation**: Frame-based. `PageService` maps ViewModel types to Page types by full name. When adding a new page, you must: (1) create the View and ViewModel, (2) register both in `App.xaml.cs`, (3) add the mapping in `PageService.Configure()`.
- **ViewModel base**: ViewModels extend `ObservableRecipient` (from CommunityToolkit.Mvvm). Use `[ObservableProperty]` and `[RelayCommand]` source generator attributes.
- **INavigationAware**: ViewModels implement this contract to receive `OnNavigatedTo`/`OnNavigatedFrom` lifecycle callbacks.
- **Server connection**: `ISubsonicClientService` (singleton) manages the `SubsonicClient` lifecycle. Credentials are persisted via `ILocalSettingsService` under key `"ServerCredential"`. On startup, `ActivationService` checks for saved credentials and navigates to `ConnectPage` or `ShellPage` accordingly (`DefaultActivationHandler` only handles fallback navigation to `MainViewModel` once the shell exists). URL resolution is intelligent: tries `https://<host>/rest` first, then bare host.
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
| **ILoggingService** | Custom sink-based logging. Category-scoped loggers via `GetLogger(name)`. Sinks: `DebugSink` (debug output), `FileSink` (per-launch log files). |
| **IBackdropSelectorService** | Persists and applies window backdrop: `None`, `Mica`, `MicaAlt`, `Acrylic`. Coordinates with theme service. |
| **IActivationService** | Startup orchestration: credential check → `ConnectPage` or `ShellPage`, theme + taskbar + backdrop init. |

### Playback architecture

- **Engine**: `Windows.Media.Playback.MediaPlayer` (native Windows media engine).
- **SMTC**: System Media Transport Controls for lock screen / media key support. `CommandManager.IsEnabled = true` for automatic integration.
- **Queue logic**: `PlaybackQueue` — pure logic class (no WinUI dependencies, fully unit-testable). Manages shuffle (Fisher-Yates), repeat (`Off`/`One`/`All`), navigation history (ID-based stack), play history (ring buffer, max 100), and queue mutations (`AddNext`, `AddToEnd`, `RemoveAt`, `MoveTrack`, `ClearUpcoming`).
- **Position tracking**: `DispatcherQueueTimer` fires every 500ms to update the `Position` property.
- **Event-driven**: PlaybackService raises events (`PlaybackStateChanged`, `TrackChanged`, `PositionChanged`); `PlayerViewModel` and `TaskbarService` subscribe.

### Shell layout

1. **Top bar** — App icon + title (left), search box with `Ctrl+K` hotkey (center), popup with 3-column results (Artists | Albums | Songs).
2. **Sidebar** — `NavigationView` with: Home, Albums, Artists, Playlists, Settings.
3. **Content area** — `Frame` for page content.
4. **Bottom bar** — Player bar defined inline in `ShellPage.xaml` (now playing info, playback buttons, seek bar, volume, queue popup).

### Subsonic API Client (`Hypersonic.Client`)

- `ISubsonicClient` / `SubsonicClient` — wrapper covering System, Browsing, Album/Song Lists, Searching, Playlists, Media Retrieval, Media Annotation, User Management, and Library Scanning APIs. Sharing, Podcast, Bookmark, Jukebox, and Chat regions are **stub placeholders** (empty `#region` blocks with no methods).
- API endpoints are defined as constants in `Constants.Action`.
- Request/Response pattern with strongly-typed DTOs in `Models/Entities/`, `Models/Requests/`, `Models/Responses/`.
- Supports both XML and JSON response formats.

## Notable implementation details

- **Incremental loading**: `IncrementalAlbumCollection` implements `ISupportIncrementalLoading` for lazy-loaded album grids (50 items per batch).
- **Search debounce**: 300ms delay via `Task.Delay` + `CancellationTokenSource` to prevent stale API calls.
- **Disc-based grouping**: `AlbumDetailViewModel` groups songs by disc number using a mixed-type collection (`DiscHeaderItem` + `SongInfo`) rendered by `TrackListTemplateSelector`.
- **Taskbar buttons**: `ITaskbarList3` COM interop renders Segoe MDL2 Assets glyphs to bitmaps via GDI+, theme-aware (light/dark). Responds to `WM_COMMAND` (0x0111) for button clicks.
- **Custom controls**: `MarqueeTextBlock` (Composition API horizontal scroll animation), `SquarePanel` (1:1 aspect ratio layout).
- **Cover art**: Loaded via `SubsonicClient.GetCoverArtUri()`, displayed as `BitmapImage` in XAML and `RandomAccessStreamReference` for SMTC.
- **DPI**: `app.manifest` sets `PerMonitorV2` DPI awareness.
- **MSIX detection**: `RuntimeHelper.IsMSIX` (P/Invoke to `kernel32.dll`) determines packaged vs unpackaged mode, affecting settings storage and log file paths.

## Logging

Custom sink-based system (not `Microsoft.Extensions.Logging`). Obtain a category-scoped `ILogger` via `ILoggingService.GetLogger(nameof(Class))`. Methods: `Debug()`, `Info()`, `Warning()`, `Error()` — each accepts `message`, optional `context` object (serialized as JSON), and optional `Exception`.

- **Sinks**: `DebugSink` (Debug output, all levels), `FileSink` (per-launch file in `Logs/` directory, Info+).
- **Log files**: MSIX → `ApplicationData.Current.LocalFolder\Logs\`; unpackaged → `%LOCALAPPDATA%\Hypersonic\Logs\`. Named `hypersonic-{yyyyMMdd-HHmmss}.log`.
- **Release mode**: `MinimumLevel` set to `Info` (debug messages suppressed).
- **Thread-safe**: `Lock`-based synchronization. Sink failures are isolated (logged to Debug, don't propagate).

## Persisted settings keys

Stored via `ILocalSettingsService` (MSIX → `ApplicationData`; unpackaged → `%LOCALAPPDATA%\Hypersonic\ApplicationData\LocalSettings.json`):

| Key | Type | Purpose |
|---|---|---|
| `"ServerCredential"` | `ServerCredential` | Server URL, username, password |
| `"AppBackgroundRequestedTheme"` | `string` (enum) | Theme: Light/Dark/Default |
| `"AppRequestedBackdrop"` | `string` (enum) | Backdrop: None/Mica/MicaAlt/Acrylic |

## MVP Roadmap

See [`ROADMAP.md`](ROADMAP.md) for the phased MVP plan — covers playback polish, persistence, full-window player with lyrics, scrobbling, playlist management, keyboard shortcuts, and stretch-goal offline caching.

## Design documentation

Feature specs and implementation guides live in `docs/`:
- `docs/design/` — Design rationale and API contracts
- `docs/implementation/` — Step-by-step implementation plans

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
- **Client tests** (`Hypersonic.Client.UnitTest`): Deserialization tests for XML/JSON API responses, client constructor validation.
- **App tests** (`Hypersonic.Tests.MSTest`): Server URL resolution logic. Uses `[UITestMethod]` for WinUI-dependent tests.
- **Unit tests** (`Hypersonic.UnitTest`): Pure logic tests (PlaybackQueue, LoggingService) — no WinUI dependencies.
- **No mocking library** — tests are integration-oriented against real serialized data.

## Error handling

- **Unhandled exceptions**: `App_UnhandledException` handler logs to `ILoggingService` before crash.
- **API client**: Catches `JsonException`/`XmlException` during deserialization, wraps as `InvalidOperationException`. `HttpRequestException` for network failures.
- **URL resolution**: Swallows `HttpRequestException` for intermediate URL candidates, propagates only on final failure.

## Code Style

Enforced via `.editorconfig`:
- **Indentation**: 4 spaces, Allman brace style (braces on new lines)
- **Naming**: PascalCase for types/members, interfaces prefixed with `I`
- **Preferences**: `var` everywhere, file-scoped namespaces, prefer braces, pattern matching over `as`+null check
- **Line endings**: CRLF

## XAML converters

Registered globally in `Themes/Generic.xaml`. Key converters: `BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `StringToBoolConverter`, `PlayPauseGlyphConverter`, `SecondsToTimeConverter`, `VolumeToPercentageConverter`, `BoolToGlyphConverter`. Note: `EnumToBooleanConverter` (in Helpers) is registered locally in `SettingsPage.xaml`, not in Generic.xaml.

## What's not in the repo

- No CI/CD pipeline (GitHub Actions, Azure Pipelines, etc.)
- No central package management (`Directory.Build.props`, `global.json`)
- No mocking library
