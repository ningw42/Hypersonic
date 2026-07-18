# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Hypersonic is a WinUI 3 desktop music streaming client for Subsonic/OpenSubsonic-compatible servers (e.g., Navidrome). Built with .NET 10, Windows App SDK 2.2.0, and MSIX packaging.

## Build & Test Commands

```bash
# Build the full solution
dotnet build Hypersonic.sln -p:Platform=x64

# Build a specific project
dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64
dotnet build Hypersonic.Client/Hypersonic.Client.csproj

# Run tests
dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj
dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64
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
| **Hypersonic** | WinUI 3 desktop app (Views, ViewModels, platform Services, Controls). References Hypersonic.Core |
| **Hypersonic.Core** | Platform-independent models, contracts, services, helpers (`net10.0`) |
| **Hypersonic.Client** | Subsonic/OpenSubsonic API client library |
| **Hypersonic.Core.Tests** | Core library tests (PlaybackQueue, LoggingService, persistence) |
| **Hypersonic.Tests** | WinUI app tests (MSTest, `[UITestMethod]`, `dotnet test` with testhost fix) |
| **Hypersonic.Client.UnitTest** | Client library tests |

## App Folder Structure

```
Hypersonic.Core/              # net10.0 class library — platform-independent code
├── Models/           # Data models and enums (AppState, PlaybackTrack, RepeatMode, etc.)
├── Contracts/
│   └── Services/     # Service interfaces (IPlaybackService, ILoggingService, etc.)
├── Helpers/          # Utilities (Json, VaultKeyHelper)
└── Services/
    └── Logging/      # Log sinks (FileSink, DebugSink)

Hypersonic/                   # WinUI 3 app — references Hypersonic.Core
├── Activation/       # Startup handlers (DefaultActivationHandler)
├── Behaviors/        # XAML Behaviors (NavigationViewHeaderBehavior)
├── Collections/      # Custom collections (IncrementalAlbumCollection)
├── Contracts/
│   ├── Services/     # WinUI service interfaces (INavigationService, ITaskbarService, etc.)
│   └── ViewModels/   # ViewModel interfaces (INavigationAware)
├── Controls/         # Custom controls (MarqueeTextBlock, SquarePanel)
├── Converters/       # XAML value converters
├── Helpers/          # WinUI utilities (TitleBarHelper, RuntimeHelper, TrackListTemplateSelector)
├── Interop/          # COM interop (ITaskbarList3 for taskbar thumbnail buttons)
├── Messages/         # WeakReferenceMessenger messages (FavoritesChangedMessage)
├── Models/           # Presentation models (SongInfo, QueueItem, DiscHeaderItem — use ObservableObject)
├── Services/         # WinUI services (PlaybackService, LocalSettingsService, etc.)
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
- **Navigation**: Frame-based. `PageService` maps ViewModel types to Page types by full name. When adding a new page, you must: (1) create the View and ViewModel, (2) register both in `App.xaml.cs`, (3) add the mapping in `PageService.Configure()`, (4) if top-level, add to `TopLevelPages` HashSet in `DefaultActivationHandler`, `ShellViewModel`, and `ConnectViewModel`.
- **ViewModel base**: ViewModels extend `ObservableRecipient` (from CommunityToolkit.Mvvm). Use `[ObservableProperty]` and `[RelayCommand]` source generator attributes.
- **INavigationAware**: ViewModels implement this contract to receive `OnNavigatedTo`/`OnNavigatedFrom` lifecycle callbacks.
- **Server connection**: `ISubsonicClientService` (singleton) manages the `SubsonicClient` lifecycle. Credentials are managed by `ICredentialService`: non-secret metadata (URL, username) stored via `ILocalSettingsService` under keys `"ServerAccounts"` and `"ActiveAccount"`, passwords stored in Windows Credential Manager (`PasswordVault`). On startup, `ActivationService` runs legacy migration, loads the active credential, tests the connection, and navigates to `ConnectPage` or `ShellPage`. URL resolution is intelligent: tries `https://<host>/rest` first, then bare host.
- **Localization**: String resources are in `Strings/en-us/Resources.resw`.

### Services

Services are split between Hypersonic.Core (platform-independent) and Hypersonic (WinUI-dependent).

**Core services** (`Hypersonic.Core.Contracts.Services` / `Hypersonic.Core.Services`):

| Service | Responsibility |
|---|---|
| **IPlaybackService** | Audio playback contract: queue, now-playing, position, volume, shuffle, repeat. |
| **ILoggingService** | Custom sink-based logging. Category-scoped loggers via `GetLogger(name)`. |
| **IAppStateService** | Persists runtime state (playback prefs, opaque WinUIEx window placement, last page) to `AppState.json`. |
| **ICredentialService** | Manages server accounts list and passwords. |
| **IFileService** | Generic file read/write/delete using JSON serialization. |
| **ILocalSettingsService** | Key-value persistence contract. |

**WinUI services** (`Hypersonic.Contracts.Services` / `Hypersonic.Services`):

| Service | Responsibility |
|---|---|
| **ISubsonicClientService** | Wraps `SubsonicClient` lifecycle. Credential validation, multi-URL resolution, connection testing. |
| **INavigationService** | Frame-based navigation with `INavigationAware` lifecycle support and back button. |
| **IPageService** | Maps ViewModel type names to Page types via dictionary lookup. |
| **INavigationViewService** | Manages NavigationView menu item selection and header updates. |
| **IThemeSelectorService** | Persists theme preference (Light/Dark/Default) and updates title bar dynamically. |
| **ITaskbarService** | Taskbar thumbnail buttons (Prev/Play-Pause/Next) via `ITaskbarList3` COM interop. |
| **IBackdropSelectorService** | Persists and applies window backdrop: `None`, `Mica`, `MicaAlt`, `Acrylic`. |
| **IActivationService** | Startup orchestration: app state init, credential migration, window restore, theme + taskbar + backdrop init. |
| **IFavoritesService** | Single writer of the in-memory starred-song set. Owns the optimistic flip + `Star/Unstar` + status check + revert once, and broadcasts each change as a batched `FavoritesChangedMessage` over `WeakReferenceMessenger`. Fed by user toggles, a `getStarred2` poll, and page-load reconciles, all serialized through one epoch/seq/lease write gate. See `docs/design/service/2026-06-07-favorite-sync.md`. |

### Cross-surface favorite (star) sync

A song's starred state is one shared fact across every surface. `FavoritesService` (singleton) is the single writer; every surface is a pure subscriber that updates only the model instances it owns whose id is in the broadcast. This is the codebase's **first use of `WeakReferenceMessenger`** (the `IMessenger` is injected, not reached statically, so tests isolate it): participating ViewModels (`PlayerViewModel` and the four track-list VMs) pass it to `base(messenger)`, set `IsActive = true`, and implement `IRecipient<FavoritesChangedMessage>`. Messages live in `Hypersonic/Messages/`. `PlaybackTrack` carries a hand-rolled `INotifyPropertyChanged` where **only `IsStarred`** notifies (Core stays dependency-free), so queue-row hearts (`x:Bind Track.IsStarred, Mode=OneWay`) live-update. Toggles delegate to `FavoritesService.ToggleAsync` rather than calling the API per-surface; the optimistic flip arrives back via the broadcast. The store seeds on connect (`ActivationService` / `ConnectViewModel` call `Start()`) and re-polls on window activation (`MainWindow` `Activated` handler → `RefreshAsync`).

### Playback architecture

- **Engine**: `Windows.Media.Playback.MediaPlayer` (native Windows media engine).
- **SMTC**: System Media Transport Controls for lock screen / media key support. `CommandManager.IsEnabled = true` for automatic integration.
- **Queue logic**: `PlaybackQueue` (in `Hypersonic.Core.Services`) — pure logic class (no WinUI dependencies, fully unit-testable). Manages shuffle (Fisher-Yates), repeat (`Off`/`One`/`All`), navigation history (ID-based stack), play history (ring buffer, max 100), and queue mutations (`AddNext`, `AddToEnd`, `RemoveAt`, `MoveTrack`, `ClearUpcoming`).
- **Position tracking**: `DispatcherQueueTimer` fires every 500ms to update the `Position` property.
- **Event-driven**: PlaybackService raises events (`PlaybackStateChanged`, `TrackChanged`, `PositionChanged`); `PlayerViewModel` and `TaskbarService` subscribe.
- **Read-only observation**: `IReadOnlyPlaybackState` (Core) is the observe half of a command/query split with `IPlaybackService`, which extends it. The same `PlaybackService` singleton is mapped to both interfaces in DI; pure observers (e.g. `NowPlayingViewModel`) depend on the read interface. `PlaybackClock` (`Hypersonic.Core.Services`) is a shared, ms-based position estimator: each surface (lyrics canvas on the render thread, player bar on the UI thread) owns its own instance fed by its own "now" source, interpolating the coarse 500 ms `Position` anchor into smooth motion. See `docs/design/service/2026-06-21-playback-progress-clock.md`.

### Pages

| Page | ViewModel | Purpose |
|---|---|---|
| **ShellPage** | ShellViewModel + PlayerViewModel | App shell: nav sidebar, player bar, content frame |
| **ConnectPage** | ConnectViewModel | Server login (URL + username + password) |
| **MainPage** | MainViewModel | Home page (currently stub) |
| **AlbumsPage** | AlbumsViewModel | Album grid with incremental loading |
| **AlbumDetailPage** | AlbumDetailViewModel | Album tracks with disc-based grouping |
| **ArtistsPage** | ArtistsViewModel | Artist list |
| **ArtistDetailPage** | ArtistDetailViewModel | Artist albums + top tracks |
| **PlaylistsPage** | PlaylistsViewModel | User playlists |
| **PlaylistDetailPage** | PlaylistDetailViewModel | Playlist tracks |
| **SettingsPage** | SettingsViewModel | Theme & backdrop selectors |

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
| `"ServerAccounts"` | `ServerAccount[]` | List of known servers (URL + username) |
| `"ActiveAccount"` | `ServerAccount` | Currently active server (URL + username) |
| `"AppBackgroundRequestedTheme"` | `string` (enum) | Theme: Light/Dark/Default |
| `"AppRequestedBackdrop"` | `string` (enum) | Backdrop: None/Mica/MicaAlt/Acrylic |

Passwords are stored in Windows Credential Manager (`PasswordVault`) under resource key `Hypersonic:<serverUrl>`, not in settings.

Runtime state (volume, mute, shuffle, repeat, WinUIEx window placement, last page) is stored separately in `AppState.json` via `IAppStateService`. Its relevant sections are `PlaybackState` (volume, mute, shuffle, repeat), `WindowState` (`Placement`, an opaque nullable string), and `NavigationState` (last page).

### Persistence patterns

- **External update guard**: `PlayerViewModel._isSavingFromExternal` flag prevents redundant saves when hydrating ViewModel properties from service state (e.g., on startup or playback state change events).
- **Player-only debounce**: `PlayerViewModel` uses a 1-second non-repeating `DispatcherQueueTimer` to batch rapid volume changes. Window placement has no application-owned debounce.
- **Window placement ownership**: WinUIEx owns placement capture, validation, and restore through the shared `PersistenceId`. `AppStateWindowPersistenceStorage` bridges WinUIEx's property bag to `AppState.Window.Placement`; when WinUIEx writes placement on a real close, the adapter synchronously persists the complete state document through `AppStateService.SaveSync()`.
- **Tray lifecycle**: Minimize-to-tray and close-to-tray only hide the live window in-session and do not read or flush placement. A later explicit tray Exit performs a real close, allowing WinUIEx to save placement; `MainWindow_Closed` separately flushes only pending `PlayerViewModel` volume state before shutdown cleanup.

## MVP Roadmap

See [`ROADMAP.md`](ROADMAP.md) for the phased MVP plan — covers playback polish, persistence, full-window player with lyrics, scrobbling, playlist management, keyboard shortcuts, and stretch-goal offline caching.

## Design documentation

Feature specs and implementation guides live in `docs/`:
- `docs/design/` — Design rationale and API contracts
- `docs/implementation/` — Step-by-step implementation plans

## Key dependencies

| Package | Version | Purpose |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | MVVM source generators (`[ObservableProperty]`, `[RelayCommand]`) |
| Microsoft.Extensions.Hosting | 10.0.9 | Dependency injection container |
| Microsoft.WindowsAppSDK | 2.2.0 | WinUI 3 framework |
| Microsoft.Xaml.Behaviors.WinUI.Managed | 3.0.1 | XAML Behaviors |
| System.Drawing.Common | 10.0.9 | GDI+ for taskbar icon rendering |
| WinUIEx | 2.9.1 | Extended WinUI utilities (`WindowEx` base class) |

JSON serialization uses `System.Text.Json` (built into .NET 10) via the `Hypersonic.Core.Helpers.Json` wrapper. `JsonStringEnumConverter` is configured globally for human-readable enum serialization.

## Testing

- **Framework**: MSTest with `coverlet.collector` for coverage.
- **Core tests** (`Hypersonic.Core.Tests`): Pure logic tests — PlaybackQueue, LoggingService, FileSink, DebugSink, persistence helpers (Json, VaultKeyHelper, AppState). No WinUI dependencies, runs with plain `dotnet test`.
- **App tests** (`Hypersonic.Tests`): WinUI-dependent tests — server URL resolution, LocalSettingsService, PlayerViewModel toggle commands. Uses `[UITestMethod]` for WinUI tests. Requires `-p:Platform=x64`. Includes testhost fix (`OutputType=Exe` + `GenerateProgramFile=true`) for dotnet/sdk#53412.
- **Client tests** (`Hypersonic.Client.UnitTest`): Deserialization tests for XML/JSON API responses, client constructor validation.
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

Registered globally in `Themes/Generic.xaml`. Key converters: `BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `StringToBoolConverter`, `PlayPauseGlyphConverter`, `SecondsToTimeConverter`, `VolumeToPercentageConverter`, `BoolToGlyphConverter`, `BoolToFavoriteBrushConverter`. Note: `EnumToBooleanConverter` (in Helpers) is registered locally in `SettingsPage.xaml`, not in Generic.xaml.

## What's not in the repo

- No CI/CD pipeline (GitHub Actions, Azure Pipelines, etc.)
- No central package management (`Directory.Build.props`, `global.json`)
- No mocking library
