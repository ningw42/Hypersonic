# Core Library Extraction — Implementation Details

Companion to [docs/design/infra/2026-04-10-core-library-extraction.md](../../design/infra/2026-04-10-core-library-extraction.md). This document records what was actually built, decisions made during implementation, and deviations from the original design.

---

## Files Changed

### New files

| File | Purpose |
|---|---|
| `Hypersonic.Core/Hypersonic.Core.csproj` | `net10.0` class library with `InternalsVisibleTo` for Core.Tests |
| `Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj` | `net10.0` test project referencing Core via ProjectReference |
| `Hypersonic.Tests/Hypersonic.Tests.csproj` | Rewritten WinUI test csproj with testhost fix (`OutputType=Exe`, `GenerateProgramFile=true`) |

### Moved files (Hypersonic → Hypersonic.Core, 30 files)

**Models/ (15 files):** `AlbumInfo.cs`, `AppState.cs`, `ArtistInfo.cs`, `BackdropType.cs`, `LocalSettingsOptions.cs`, `LogEntry.cs`, `LogLevel.cs`, `PlaybackTrack.cs`, `PlaylistInfo.cs`, `QueueSource.cs`, `RepeatMode.cs`, `SearchSuggestionItem.cs`, `ServerAccount.cs`, `ServerCredential.cs`, `SuggestionItem.cs`

**Contracts/Services/ (8 files):** `IAppStateService.cs`, `ICredentialService.cs`, `IFileService.cs`, `ILocalSettingsService.cs`, `ILogger.cs`, `ILoggingService.cs`, `ILogSink.cs`, `IPlaybackService.cs`

**Helpers/ (2 files):** `Json.cs`, `VaultKeyHelper.cs`

**Services/ (5 files):** `PlaybackQueue.cs`, `FileService.cs`, `LoggingService.cs`, `Logging/DebugSink.cs`, `Logging/FileSink.cs`

All moved files had their namespaces updated from `Hypersonic.*` to `Hypersonic.Core.*` and internal cross-references updated accordingly.

### Moved test files

| From | To | New namespace |
|---|---|---|
| `Hypersonic.UnitTest/PlaybackQueueTests.cs` | `Hypersonic.Core.Tests/Services/PlaybackQueueTests.cs` | `Hypersonic.Core.Tests.Services` |
| `Hypersonic.UnitTest/LoggingServiceTest.cs` | `Hypersonic.Core.Tests/Services/LoggingServiceTest.cs` | `Hypersonic.Core.Tests.Services` |
| `Hypersonic.UnitTest/PersistenceTests.cs` | `Hypersonic.Core.Tests/Models/PersistenceTests.cs` | `Hypersonic.Core.Tests.Models` |
| `Hypersonic.UnitTest/LocalSettingsServiceTests.cs` | `Hypersonic.Tests/Services/LocalSettingsServiceTests.cs` | `Hypersonic.Tests.Services` |
| `Hypersonic.Tests.MSTest/ResolveServerUrlsTest.cs` | `Hypersonic.Tests/Services/ResolveServerUrlsTest.cs` | `Hypersonic.Tests.Services` |
| `Hypersonic.Tests.MSTest/PlayerViewModelToggleTests.cs` | `Hypersonic.Tests/ViewModels/PlayerViewModelToggleTests.cs` | `Hypersonic.Tests.ViewModels` |
| `Hypersonic.Tests.MSTest/Initialize.cs` | `Hypersonic.Tests/Initialize.cs` | `Hypersonic.Tests` |

### Deleted files

| File | Reason |
|---|---|
| `Hypersonic.UnitTest/` (entire directory) | Replaced by `Hypersonic.Core.Tests` (proper ProjectReference instead of source-linked files) |
| `Hypersonic.Tests.MSTest/` (directory, renamed) | Renamed to `Hypersonic.Tests/` with rewritten csproj |
| `Hypersonic.Tests.MSTest/TestClass.cs` | Scaffold boilerplate, not needed |
| `Hypersonic.Tests.MSTest/Usings.cs` | Replaced by `<Using>` items in new csproj |
| `Hypersonic.Tests.MSTest/README.md` | Template documentation, not needed |

### Modified files (namespace updates)

**Hypersonic.csproj:**
- Changed `InternalsVisibleTo` from `Hypersonic.Tests.MSTest` to `Hypersonic.Tests`
- Added `ProjectReference` to `Hypersonic.Core`

**App.xaml.cs:** Added `using Hypersonic.Core.Services;`, `using Hypersonic.Core.Services.Logging;`, `using Hypersonic.Core.Models;`, `using Hypersonic.Core.Contracts.Services;`. Kept `using Hypersonic.Services;` (for WinUI service concrete types). Removed `using Hypersonic.Services.Logging;`, `using Hypersonic.Models;`.

**MainWindow.xaml.cs:** Replaced `using Hypersonic.Services;` with `using Hypersonic.Core.Services;` (only uses `LoggingService` concrete type). Added `using Hypersonic.Core.Contracts.Services;`.

**Models/QueueItem.cs:** Added `using Hypersonic.Core.Models;` — `QueueItem` stays in `Hypersonic.Models` but references `PlaybackTrack` which moved to Core.

**C# files with namespace additions (~35 files):** ViewModels, Services, Converters, Collections, Activation, code-behind files received `using Hypersonic.Core.Models;` and/or `using Hypersonic.Core.Contracts.Services;` as needed. Files using only moved types had their old `using Hypersonic.Models;` replaced; files using both moved and staying types (`SongInfo`, `QueueItem`, `DiscHeaderItem`) retained both usings.

**Helpers/SettingsStorageExtensions.cs:** Added `using Hypersonic.Core.Helpers;` — uses `Json` which moved to Core. This file was not anticipated in the design but the compiler caught it immediately.

**XAML files (2 of 4 changed):**
- `Views/SettingsPage.xaml`: Changed `xmlns:models="using:Hypersonic.Models"` to `xmlns:models="using:Hypersonic.Core.Models"`
- `Views/ShellPage.xaml`: Added `xmlns:coremodels="using:Hypersonic.Core.Models"`, updated `SearchSuggestionItem` references from `models:` to `coremodels:` prefix

**Hypersonic.sln:** Removed `Hypersonic.UnitTest` and `Hypersonic.Tests.MSTest` project entries. Added `Hypersonic.Core` and `Hypersonic.Core.Tests` (Any CPU configurations) and `Hypersonic.Tests` (platform-specific configurations).

**CLAUDE.md:** Updated solution structure, build/test commands, folder structure, services table, playback architecture section, Json reference, and testing section.

**AGENTS.md:** Updated project structure, test commands, and testing guidelines to reflect new project names and ProjectReference model.

---

## Design Deviations

### XAML xmlns: second prefix instead of straight replacement

The design spec says to update `xmlns:models="using:Hypersonic.Models"` to `xmlns:models="using:Hypersonic.Core.Models"` in all 4 XAML files. Only 2 of the 4 were changed.

**Why:** The design's straight replacement would break compilation. `AlbumDetailPage.xaml` uses `models:DiscHeaderItem` and `models:SongInfo` — both stay in `Hypersonic.Models`. `PlaylistDetailPage.xaml` uses `models:SongInfo` — also stays. Changing their `xmlns:models` to `Hypersonic.Core.Models` would make these types unresolvable. `ShellPage.xaml` uses both `QueueItem` (stays) and `SearchSuggestionItem` (moves) from `models:`, so it needs two xmlns prefixes. Only `SettingsPage.xaml` (uses only `BackdropType`, which moves) gets the straight replacement.

Result:
- `AlbumDetailPage.xaml`: no change (all referenced types stay)
- `PlaylistDetailPage.xaml`: no change (all referenced types stay)
- `SettingsPage.xaml`: straight replacement to `Hypersonic.Core.Models`
- `ShellPage.xaml`: kept `xmlns:models` for staying types, added `xmlns:coremodels` for moved types

### `SettingsStorageExtensions.cs` required an additional update

The design spec's namespace impact section does not mention this file. It uses `Json` from `Hypersonic.Helpers`, which moved to `Hypersonic.Core.Helpers`.

**Why:** The design's file inventory focused on files with explicit `using Hypersonic.Helpers;` that use `Json` or `VaultKeyHelper`. `SettingsStorageExtensions.cs` was in the same `Hypersonic.Helpers` namespace as `Json`, so it resolved `Json` without a `using` statement. After the move, it needed an explicit `using Hypersonic.Core.Helpers;`. The compiler flagged this immediately.

### Helpers namespace split requires dual usings in some files

The design spec says "add `using Hypersonic.Core.Helpers;` as needed" but does not enumerate which files need both old and new Helpers usings.

**Why:** `Json` and `VaultKeyHelper` moved to `Hypersonic.Core.Helpers`; `RuntimeHelper`, `TitleBarHelper`, `FrameExtensions`, etc. stay in `Hypersonic.Helpers`. Files that use helpers from both sides (e.g., `AppStateService` uses `Json` from Core and `RuntimeHelper` from Hypersonic) need both `using` statements. Files that only use moved helpers (e.g., `CredentialService` uses only `VaultKeyHelper`) got a straight replacement.

### Contracts namespace split requires dual usings in most consumers

The design spec says "add `using Hypersonic.Core.Contracts.Services;` as needed" but does not detail which files need both.

**Why:** Service interfaces are split 8/8. Most ViewModels and Services inject interfaces from both sides (e.g., `IPlaybackService` from Core + `INavigationService` from Hypersonic). Nearly every file that had `using Hypersonic.Contracts.Services;` also needed `using Hypersonic.Core.Contracts.Services;`. Files that only use staying interfaces (e.g., `PageService`, `NavigationViewService`, `NavigationViewHeaderBehavior`) did not need the Core using.

### BOM-prefixed files required manual namespace fixes

Two files (`LocalSettingsOptions.cs`, `ILocalSettingsService.cs`) had UTF-8 BOM characters that caused the batch `sed` namespace replacement to silently fail. The `sed` regex `^namespace Hypersonic\.Models;` did not match because the BOM byte sequence preceded the `namespace` keyword.

**Why:** These files were originally created by Visual Studio, which adds BOM to new files by default. The other 28 moved files either lacked BOM or had their namespace on a line without leading BOM. The issue was caught during the intermediate build step — the compiler reported `LocalSettingsOptions` and `PlaybackQueue` as unresolvable, leading to diagnosis and manual fix.

---

## Implementation Notes

### Namespace update strategy

The design spec describes the namespace changes as "mechanical (find-and-replace)". In practice, the update required per-file analysis because:

1. **Models split:** `SongInfo`, `QueueItem`, `DiscHeaderItem` stay in `Hypersonic.Models` with `CommunityToolkit.Mvvm` dependency. Files using only moved types get a straight replacement; files using both get dual `using` statements.

2. **Contracts split:** 8 interfaces move, 8 stay. Files consuming interfaces from both sides need both namespaces. Each file was checked for which specific interfaces it references.

3. **Helpers split:** `Json` and `VaultKeyHelper` move; 7 other helpers stay. Files were checked for which specific helper types they use.

4. **Services split:** `PlaybackQueue`, `FileService`, `LoggingService`, and logging sinks move to `Hypersonic.Core.Services`. `PlaybackService.cs` and `App.xaml.cs` needed `using Hypersonic.Core.Services;` for the concrete types. `MainWindow.xaml.cs` also needed it for the `LoggingService` dispose-on-close pattern.

### Test redistribution rationale

The old `Hypersonic.UnitTest` project used 22 source-linked `<Compile Include="..">` entries to share code with the WinUI app without a ProjectReference. This workaround is no longer needed:

- **Core tests** (`Hypersonic.Core.Tests`): Reference `Hypersonic.Core` via ProjectReference. All 3 test files that tested Core code moved here: `PlaybackQueueTests` (74 test methods), `LoggingServiceTest` (3 test classes), `PersistenceTests` (9 tests).

- **WinUI tests** (`Hypersonic.Tests`): Reference `Hypersonic` via ProjectReference (Core available transitively). `LocalSettingsServiceTests` moved here because `LocalSettingsService` stays in Hypersonic (uses `#if WINDOWS` and `RuntimeHelper.IsMSIX`). `ResolveServerUrlsTest` and `PlayerViewModelToggleTests` were already in the WinUI test project.

### Testhost fix for `Hypersonic.Tests`

The new `Hypersonic.Tests.csproj` includes two overrides not present in the old `Hypersonic.Tests.MSTest.csproj`:

```xml
<OutputType>Exe</OutputType>
<GenerateProgramFile>true</GenerateProgramFile>
```

**Why:** `Microsoft.NET.Test.Sdk` 18.3.0 skips `OutputType=Exe` for `UseWinUI=true` projects, and the .NET SDK 10.0.201 lacks `testhost-10.0.runtimeconfig.json` (dotnet/sdk#53412). Without these overrides, `dotnet test` fails to launch the test host. This was verified end-to-end during design and confirmed working during implementation. The overrides can be removed once the upstream SDK bug is fixed.

The csproj also switches from split `MSTest.TestAdapter` + `MSTest.TestFramework` packages to the unified `MSTest` 4.1.0 meta-package, and moves global usings from `Usings.cs` to `<Using>` items in the csproj.

---

## Verification Results

### Build

`dotnet build Hypersonic.sln -p:Platform=x64` — 0 errors. Pre-existing warnings include: CS8618/CS8600/CS8602/CS8603 nullable warnings in `Hypersonic.Client`, MVVMTK0045 AOT warnings from `CommunityToolkit.Mvvm` field-backed `[ObservableProperty]` in `PlayerViewModel`, CS0436 type-conflict warnings from `WindowsAppSDK` auto-initializer in `Hypersonic.Tests`, and CS0067 unused-event warnings in `PlayerViewModelToggleTests` fake services. No new warnings introduced by this change.

### Automated tests

| Test project | Result |
|---|---|
| `Hypersonic.Core.Tests` | 89 passed |
| `Hypersonic.Tests` | 24 passed |
| `Hypersonic.Client.UnitTest` | 32 passed (unaffected) |

Total: 145 tests, 0 failures. Test count matches pre-refactor totals (89 + 24 + 32 = 145).
