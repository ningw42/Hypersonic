# Player Bar Favorite (Heart) Button — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an always-visible heart button to the player bar that toggles the favorite (star) state of the now-playing track, placed between the seek bar's duration text and the queue flyout toggle.

**Architecture:** Carry `IsStarred` on `PlaybackTrack` (populated from `SongInfo.IsStarred` at the build sites) so the player always knows the current track's star state. `PlayerViewModel` gains an `IsStarred` observable property hydrated on track change and a `ToggleStarCommand` that optimistically flips state and calls the Subsonic `Star`/`Unstar` API (reverting on failure) — mirroring the existing track-list `ToggleStar` pattern exactly. The button's icon is driven by converters: an existing `BoolToGlyphConverter` for the glyph and a new `BoolToFavoriteBrushConverter` for the red/secondary color.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 1.8), CommunityToolkit.Mvvm source generators, MSTest.

**Design reference:** [docs/design/ui/2026-06-07-player-bar-favorite-button.md](../../design/ui/2026-06-07-player-bar-favorite-button.md)

**Build/test commands** (run from repo root):
- Core lib tests (fast, no platform): `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`
- App tests (needs platform): `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
- Build main app: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
- Single test filter: append `--filter "FullyQualifiedName~TestName"`

---

## Task 1: Carry `IsStarred` on `PlaybackTrack`

**Files:**
- Modify: `Hypersonic.Core/Models/PlaybackTrack.cs`
- Modify: `Hypersonic/ViewModels/TracksViewModel.cs` (BuildPlaybackTracks, ~line 446)
- Modify: `Hypersonic/ViewModels/AlbumDetailViewModel.cs` (BuildPlaybackTracks, ~line 366)
- Modify: `Hypersonic/ViewModels/PlaylistDetailViewModel.cs` (BuildPlaybackTracks, ~line 228)
- Modify: `Hypersonic/ViewModels/ArtistDetailViewModel.cs` (BuildTopSongsPlaybackTracks, ~line 608)
- Test: `Hypersonic.Tests/ViewModels/TracksViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test method to `TracksViewModelTests.cs`, immediately after the existing `BuildPlaybackTracks_PropagatesTechnicalFields` test (before the final `#endregion`):

```csharp
[TestMethod]
public void BuildPlaybackTracks_PropagatesIsStarred()
{
    var vm = CreateVm(new FakeSubsonicClient());
    vm._allSongs = new List<SongInfo>
    {
        new SongInfo { Id = "s1", Title = "Starred", Artist = "A", Album = "Al", IsStarred = true },
        new SongInfo { Id = "s2", Title = "Plain", Artist = "A", Album = "Al", IsStarred = false },
    };

    var tracks = vm.BuildPlaybackTracks();

    Assert.IsTrue(tracks[0].IsStarred, "Starred song must propagate IsStarred = true");
    Assert.IsFalse(tracks[1].IsStarred, "Unstarred song must propagate IsStarred = false");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~BuildPlaybackTracks_PropagatesIsStarred"`
Expected: FAIL — compile error `'PlaybackTrack' does not contain a definition for 'IsStarred'`.

- [ ] **Step 3: Add the field to the model**

In `Hypersonic.Core/Models/PlaybackTrack.cs`, add after the `SamplingRate` property (line 23):

```csharp
    public int SamplingRate { get; set; }
    public bool IsStarred { get; set; }
```

- [ ] **Step 4: Propagate at all four build sites**

In each of the four `new PlaybackTrack { ... }` initializers, add `IsStarred = s.IsStarred,` immediately after the `SamplingRate = s.SamplingRate,` line:

- `Hypersonic/ViewModels/TracksViewModel.cs` (~line 446)
- `Hypersonic/ViewModels/AlbumDetailViewModel.cs` (~line 366)
- `Hypersonic/ViewModels/PlaylistDetailViewModel.cs` (~line 228)
- `Hypersonic/ViewModels/ArtistDetailViewModel.cs` (~line 608, method `BuildTopSongsPlaybackTracks`)

Each becomes:

```csharp
                SamplingRate = s.SamplingRate,
                IsStarred = s.IsStarred,
            };
```

(No change to `ShellViewModel.HandleSuggestionChosen` — search suggestions carry no star state, so `IsStarred` correctly defaults to `false`.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~BuildPlaybackTracks_PropagatesIsStarred"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Hypersonic.Core/Models/PlaybackTrack.cs Hypersonic/ViewModels/TracksViewModel.cs Hypersonic/ViewModels/AlbumDetailViewModel.cs Hypersonic/ViewModels/PlaylistDetailViewModel.cs Hypersonic/ViewModels/ArtistDetailViewModel.cs Hypersonic.Tests/ViewModels/TracksViewModelTests.cs
git commit -m "feat(player): carry IsStarred on PlaybackTrack"
```

---

## Task 2: `BoolToFavoriteBrushConverter`

**Files:**
- Create: `Hypersonic/Converters/BoolToFavoriteBrushConverter.cs`
- Modify: `Hypersonic/Themes/Generic.xaml` (converter registrations, ~line 15)

**Note:** This converter returns a XAML `Brush` resolved against the element's actual theme, so it must run on the UI thread and is verified manually in Task 5 rather than unit-tested (the project does not unit-test converters that touch theme resources). The neutral brush is resolved via `BoolToAccentBrushConverter.GetThemedBrush`, **not** `Application.Current.Resources[...]` — the latter doesn't track the element's `ActualTheme`, so the hollow heart would not follow a light/dark toggle. The binding is re-evaluated on theme change because `PlayerViewModel.NotifyThemeChanged()` re-raises `IsStarred` (Task 3).

- [ ] **Step 1: Create the converter**

Create `Hypersonic/Converters/BoolToFavoriteBrushConverter.cs`:

```csharp
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace Hypersonic.Converters;

/// <summary>
/// Returns the favorite-red brush when starred, otherwise the secondary text brush.
/// Used by the player bar heart button so the filled heart reads red and the
/// outline heart reads as a muted, neutral icon.
/// </summary>
public class BoolToFavoriteBrushConverter : IValueConverter
{
    // Same red as the track-list FavoriteToggleButtonStyle checked state.
    // Theme-independent, so a single shared brush is fine.
    private static readonly SolidColorBrush FavoriteBrush =
        new(Color.FromArgb(0xFF, 0xE7, 0x48, 0x56));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
        {
            return FavoriteBrush;
        }

        // Resolve against the element's actual theme (not Application.Current.Resources,
        // which doesn't track element-level theme). Re-run on theme change because
        // PlayerViewModel.NotifyThemeChanged() re-raises IsStarred.
        return BoolToAccentBrushConverter.GetThemedBrush("TextFillColorSecondaryBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 2: Register it globally**

In `Hypersonic/Themes/Generic.xaml`, add alongside the other converter registrations (immediately after the `BoolToGlyphConverter` line, ~line 15):

```xml
    <converters:BoolToGlyphConverter x:Key="BoolToGlyphConverter"/>
    <converters:BoolToFavoriteBrushConverter x:Key="BoolToFavoriteBrushConverter"/>
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Hypersonic/Converters/BoolToFavoriteBrushConverter.cs Hypersonic/Themes/Generic.xaml
git commit -m "feat(player): add BoolToFavoriteBrushConverter"
```

---

## Task 3: `PlayerViewModel` — star state, toggle command, hydration

**Files:**
- Create: `Hypersonic.Tests/Fakes/NullLoggingService.cs`
- Create: `Hypersonic.Tests/Fakes/FakeSubsonicClientService.cs`
- Create: `Hypersonic.Tests/Fakes/RecordingSubsonicClient.cs`
- Modify: `Hypersonic/ViewModels/PlayerViewModel.cs`
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelToggleTests.cs` (construction helper)
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelMetadataTests.cs` (construction)
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelRefreshQueueTests.cs` (construction)
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelJumpToQueueIndexTests.cs` (construction)
- Test: `Hypersonic.Tests/ViewModels/PlayerViewModelFavoriteTests.cs` (new)

### 3a. Shared test fakes

- [ ] **Step 1: Create `NullLoggingService` shared fake**

Create `Hypersonic.Tests/Fakes/NullLoggingService.cs`:

```csharp
using Hypersonic.Core.Contracts.Services;

namespace Hypersonic.Tests.Fakes;

public sealed class NullLogger : ILogger
{
    public void Debug(string message, object? context = null, Exception? exception = null) { }
    public void Info(string message, object? context = null, Exception? exception = null) { }
    public void Warning(string message, object? context = null, Exception? exception = null) { }
    public void Error(string message, object? context = null, Exception? exception = null) { }
}

public sealed class NullLoggingService : ILoggingService
{
    public LogLevel MinimumLevel { get; set; }
    public bool IsEnabled(LogLevel level) => false;
    public void AddSink(ILogSink sink, LogLevel minimumLevel = LogLevel.Debug) { }
    public void RemoveSink(ILogSink sink) { }
    public ILogger GetLogger(string category) => new NullLogger();
}
```

(Verify the `ILogger` method signatures against `Hypersonic.Core/Contracts/Services/ILoggingService.cs`; the four `Debug/Info/Warning/Error` overloads with `(string, object?, Exception?)` match the existing per-file `NullLogger` fakes used elsewhere in the test project.)

- [ ] **Step 2: Create `FakeSubsonicClientService` shared fake**

Create `Hypersonic.Tests/Fakes/FakeSubsonicClientService.cs`:

```csharp
using Hypersonic.Client;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Tests.Fakes;

public sealed class FakeSubsonicClientService : ISubsonicClientService
{
    public ISubsonicClient? Client { get; set; }
    public string? ActiveUserIdentity { get; set; }
    public void CreateClient(ServerCredential credential) { }
    public Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential) =>
        throw new NotImplementedException();
}
```

- [ ] **Step 3: Create `RecordingSubsonicClient` fake**

This implements the full `ISubsonicClient` interface, recording `Star`/`Unstar` calls and supporting a forced failure. Create `Hypersonic.Tests/Fakes/RecordingSubsonicClient.cs`. Copy the complete member list verbatim from the proven fake in `Hypersonic.Tests/ViewModels/ArtistDetailViewModelTopSongsTests.cs` (the nested `FakeSubsonicClient` class), keeping its `StarShouldThrow`, `StarCallCount`, `UnstarCallCount`, `StarAsync`, and `UnstarAsync` members, and stubbing every other interface member with `throw new NotImplementedException();`. Capture the last starred id as well:

```csharp
using Hypersonic.Client;
using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Tests.Fakes;

/// <summary>
/// Minimal ISubsonicClient test double that records Star/Unstar calls and can
/// be forced to throw. All other members throw NotImplementedException.
/// </summary>
public sealed class RecordingSubsonicClient : ISubsonicClient
{
    public bool StarShouldThrow { get; set; }
    public int StarCallCount { get; private set; }
    public int UnstarCallCount { get; private set; }
    public string? LastStarredId { get; private set; }
    public string? LastUnstarredId { get; private set; }

    /// <summary>Status returned in the SubsonicResponse from Star/Unstar ("ok" or "failed").</summary>
    public string ResponseStatus { get; set; } = "ok";

    /// <summary>When set, Star/Unstar await this gate before completing — lets a test
    /// interleave a track change mid-request to exercise the revert race.</summary>
    public TaskCompletionSource<bool>? Gate { get; set; }

    public async Task<SubsonicResponse> StarAsync(string? id = null, string? albumId = null, string? artistId = null)
    {
        if (Gate is not null) await Gate.Task;
        if (StarShouldThrow) throw new HttpRequestException("Star failed");
        StarCallCount++;
        LastStarredId = id;
        return new SubsonicResponse { Status = ResponseStatus };
    }

    public async Task<SubsonicResponse> UnstarAsync(string? id = null, string? albumId = null, string? artistId = null)
    {
        if (Gate is not null) await Gate.Task;
        if (StarShouldThrow) throw new HttpRequestException("Unstar failed");
        UnstarCallCount++;
        LastUnstarredId = id;
        return new SubsonicResponse { Status = ResponseStatus };
    }

    // --- All other ISubsonicClient members: copy the NotImplementedException
    //     stub block verbatim from ArtistDetailViewModelTopSongsTests.cs's
    //     nested FakeSubsonicClient (the "Unused stubs" region, including
    //     GetCoverArtUri / GetStreamUri / GetAvatarUri). ---
}
```

The stub block to copy spans every other `ISubsonicClient` method (`PingAsync`, `GetLicenseAsync`, … `GetAvatarUri`). After pasting, the build will tell you if any interface member is missing. (`ResponseStatus` defaults to `"ok"` so the happy-path tests don't need to set it; `Gate` and `ResponseStatus` exist for the failed-status/race guard tests below.)

### 3b. PlayerViewModel changes (write tests first)

- [ ] **Step 4: Write the failing favorite tests**

Create `Hypersonic.Tests/ViewModels/PlayerViewModelFavoriteTests.cs`.

**Important:** use a *local* `FakePlaybackService` (not the shared `Hypersonic.Tests.Fakes.FakePlaybackService`). `RaiseTrackChanged()` triggers `OnTrackChanged → RefreshQueue`, which calls `GetEffectiveQueue()` and `EffectiveQueueIndex` — the shared fake throws `NotImplementedException` for those, so the tests need a fake that returns safe empties (matching the local fakes already used in `PlayerViewModelToggleTests`/`PlayerViewModelMetadataTests`). The nested `FakePlaybackService` below takes precedence over the imported one within this class.

```csharp
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Tests.Fakes;
using Hypersonic.ViewModels;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class PlayerViewModelFavoriteTests
{
    private sealed class FakePlaybackService : IPlaybackService
    {
        public PlaybackTrack? NowPlaying { get; set; }
        public IReadOnlyList<PlaybackTrack> Queue => Array.Empty<PlaybackTrack>();
        public int QueueIndex => -1;
        public int EffectiveQueueIndex => -1;
        public bool IsPlaying { get; set; }
        public double Position { get; set; }
        public double Duration { get; set; }
        public double Volume { get; set; } = 1.0;
        public bool IsMuted { get; set; }
        public bool HasNext { get; set; }
        public bool HasPrevious { get; set; }
        public QueueSource? Source => null;
        public bool ShuffleEnabled { get; set; }
        public RepeatMode RepeatMode { get; set; }
        public IReadOnlyList<PlaybackTrack> History => Array.Empty<PlaybackTrack>();

        public event EventHandler? PlaybackStateChanged;
        public event EventHandler<TrackChangedEventArgs>? TrackChanged;
        public event EventHandler<TrackEndedEventArgs>? TrackEnded;
        public event EventHandler? PositionChanged;
        public event EventHandler? QueueChanged;

        public void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null) { }
        public void Resume() { }
        public void Pause() { }
        public void Stop() { }
        public void NextTrack() { }
        public void PreviousTrack() { }
        public void Seek(double positionSeconds) { }
        public void AddNext(IList<PlaybackTrack> tracks) { }
        public void AddToEnd(IList<PlaybackTrack> tracks) { }
        public void RemoveAt(int index) { }
        public void MoveTrack(int fromIndex, int toIndex) { }
        public void ClearUpcoming() { }
        public void ClearAll() { }
        public void JumpToTrack(string trackId) { }
        public void JumpToQueueIndex(int effectiveIndex) { }
        public IReadOnlyList<PlaybackTrack> GetEffectiveQueue() => Array.Empty<PlaybackTrack>();

        public void RaiseTrackChanged() =>
            TrackChanged?.Invoke(this, new TrackChangedEventArgs(TrackChangeDirection.Jump));
    }

    private sealed class FakeAppStateService : IAppStateService
    {
        public AppState State { get; } = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
        public Task SavePlaybackAsync() => Task.CompletedTask;
        public Task SaveWindowAsync() => Task.CompletedTask;
        public Task SaveNavigationAsync() => Task.CompletedTask;
        public Task SaveGeneralAsync() => Task.CompletedTask;
        public void SaveSync() { }
    }

    private static (PlayerViewModel vm, FakePlaybackService playback, RecordingSubsonicClient client)
        CreateViewModel(PlaybackTrack? nowPlaying = null)
    {
        var playback = new FakePlaybackService { NowPlaying = nowPlaying };
        var client = new RecordingSubsonicClient();
        var clientService = new FakeSubsonicClientService { Client = client };
        var vm = new PlayerViewModel(playback, new FakeAppStateService(), clientService,
            new NullLoggingService(), createTimer: false);
        return (vm, playback, client);
    }

    [TestMethod]
    public void OnTrackChanged_HydratesIsStarred()
    {
        var (vm, playback, _) = CreateViewModel();

        playback.NowPlaying = new PlaybackTrack { Id = "s1", Title = "T", IsStarred = true };
        playback.RaiseTrackChanged();
        Assert.IsTrue(vm.IsStarred, "IsStarred must hydrate from the starred track");

        playback.NowPlaying = new PlaybackTrack { Id = "s2", Title = "T2", IsStarred = false };
        playback.RaiseTrackChanged();
        Assert.IsFalse(vm.IsStarred, "IsStarred must update to the new unstarred track");
    }

    [TestMethod]
    public void OnTrackChanged_NoTrack_IsStarredFalse()
    {
        var (vm, playback, _) = CreateViewModel(new PlaybackTrack { Id = "s1", IsStarred = true });
        playback.RaiseTrackChanged(); // hydrate true

        playback.NowPlaying = null;
        playback.RaiseTrackChanged();

        Assert.IsFalse(vm.IsStarred, "No track => IsStarred false");
    }

    [TestMethod]
    public async Task ToggleStar_NotStarred_CallsStarAndFlips()
    {
        var track = new PlaybackTrack { Id = "s1", Title = "T", IsStarred = false };
        var (vm, _, client) = CreateViewModel(track);
        vm.IsStarred = false;

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsStarred, "VM flag flips to true");
        Assert.IsTrue(track.IsStarred, "Live PlaybackTrack flag flips to true");
        Assert.AreEqual(1, client.StarCallCount);
        Assert.AreEqual("s1", client.LastStarredId);
    }

    [TestMethod]
    public async Task ToggleStar_Starred_CallsUnstar()
    {
        var track = new PlaybackTrack { Id = "s1", Title = "T", IsStarred = true };
        var (vm, _, client) = CreateViewModel(track);
        vm.IsStarred = true;

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsStarred);
        Assert.IsFalse(track.IsStarred);
        Assert.AreEqual(1, client.UnstarCallCount);
    }

    [TestMethod]
    public async Task ToggleStar_Failure_Reverts()
    {
        var track = new PlaybackTrack { Id = "s1", Title = "T", IsStarred = false };
        var (vm, _, client) = CreateViewModel(track);
        vm.IsStarred = false;
        client.StarShouldThrow = true;

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsStarred, "VM flag reverts on failure");
        Assert.IsFalse(track.IsStarred, "Live track flag reverts on failure");
    }

    [TestMethod]
    public async Task ToggleStar_NoTrack_NoOp()
    {
        var (vm, _, client) = CreateViewModel(nowPlaying: null);

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.AreEqual(0, client.StarCallCount);
        Assert.AreEqual(0, client.UnstarCallCount);
    }
}
```

> **Also add these guard tests** (they pin the hardened behavior so it can't regress):
> - `ToggleStar_ApiReturnsFailedStatus_Reverts` — set `client.ResponseStatus = "failed"`, toggle, assert both `vm.IsStarred` and `track.IsStarred` reverted. Requires `RecordingSubsonicClient` to return `new SubsonicResponse { Status = ResponseStatus }` (default `"ok"`).
> - `ToggleStar_TrackChangesDuringFailedRequest_DoesNotClobberNewTrack` — set `client.Gate = new TaskCompletionSource<bool>()` and `client.StarShouldThrow = true`, start the toggle, swap `playback.NowPlaying` to a starred track B and `RaiseTrackChanged()`, release the gate, then assert `vm.IsStarred` still reflects B and track A reverted. Requires `RecordingSubsonicClient.StarAsync`/`UnstarAsync` to `await Gate.Task` (when set) before completing.
> - `NotifyThemeChanged_RaisesIsStarred` — subscribe to `PropertyChanged`, call `vm.NotifyThemeChanged()`, assert `IsStarred` was raised (so the hollow heart re-resolves its theme brush).


Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~PlayerViewModelFavoriteTests"`
Expected: FAIL — compile errors (the 5-arg constructor, `IsStarred`, and `ToggleStarCommand` do not exist yet).

- [ ] **Step 6: Update `PlayerViewModel` — dependencies + constructors**

In `Hypersonic/ViewModels/PlayerViewModel.cs`:

Add the using for the WinUI service contract near the top:

```csharp
using Hypersonic.Contracts.Services;
```

Add fields next to the existing ones (after `_appStateService`, line 15):

```csharp
    private readonly IAppStateService _appStateService;
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ILogger _log;
```

Replace the two constructors (lines 131–184) so the new dependencies are threaded through:

```csharp
    public PlayerViewModel(
        IPlaybackService playbackService,
        IAppStateService appStateService,
        ISubsonicClientService subsonicClientService,
        ILoggingService logService)
        : this(playbackService, appStateService, subsonicClientService, logService, createTimer: true)
    {
    }

    internal PlayerViewModel(
        IPlaybackService playbackService,
        IAppStateService appStateService,
        ISubsonicClientService subsonicClientService,
        ILoggingService logService,
        bool createTimer)
    {
        _playbackService = playbackService;
        _appStateService = appStateService;
        _subsonicClientService = subsonicClientService;
        _log = logService.GetLogger(nameof(PlayerViewModel));
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playbackService.TrackChanged += OnTrackChanged;
        _playbackService.PositionChanged += OnPositionChanged;
        _playbackService.QueueChanged += OnQueueChanged;
        // ... (rest of the existing constructor body is unchanged) ...
```

Leave the remainder of the constructor body intact. In the existing hydration block at the end of the constructor (inside `if (track != null)`), add the star hydration alongside the other field copies:

```csharp
            IsPlaying = _playbackService.IsPlaying;
            IsStarred = track.IsStarred;
```

`ILogger` and `ILoggingService` resolve from the already-imported `Hypersonic.Core.Contracts.Services` namespace (no new using needed for those).

- [ ] **Step 7: Add the `IsStarred` observable property**

Add next to the other `[ObservableProperty]` declarations (e.g. after `HasTrack`, line 96):

```csharp
    [ObservableProperty]
    public partial bool IsStarred { get; set; }
```

- [ ] **Step 8: Hydrate `IsStarred` in `OnTrackChanged`**

In `OnTrackChanged` (line 406), add alongside the other `track?.` field copies (e.g. after the `HasTrack = track != null;` line):

```csharp
        HasTrack = track != null;
        IsStarred = track?.IsStarred ?? false;
```

- [ ] **Step 9: Add the `ToggleStarAsync` command**

Add next to the other `[RelayCommand]` methods (e.g. after `ToggleMute`, line 260):

```csharp
    [RelayCommand]
    private async Task ToggleStarAsync()
    {
        var track = _playbackService.NowPlaying;
        if (!HasTrack || track is null || string.IsNullOrEmpty(track.Id))
        {
            return;
        }

        var client = _subsonicClientService.Client;
        if (client is null)
        {
            return;
        }

        var wasStarred = IsStarred;
        IsStarred = !wasStarred;
        track.IsStarred = !wasStarred;

        try
        {
            var response = wasStarred
                ? await client.UnstarAsync(id: track.Id)
                : await client.StarAsync(id: track.Id);

            // Subsonic returns HTTP 200 even on logical failure (status="failed");
            // the client only throws on transport errors, so check the status too.
            if (response.Status == "failed")
            {
                RevertStar(track, wasStarred);
                _log.Warning(
                    "Star toggle returned a failed status",
                    new { TrackId = track.Id, wasStarred, Code = response.Error?.Code, Message = response.Error?.Message });
            }
        }
        catch (Exception ex)
        {
            RevertStar(track, wasStarred);
            _log.Warning("Failed to toggle star", new { TrackId = track.Id, wasStarred }, ex);
        }
    }

    // Always restore the captured track's own state, but only refresh the visible
    // heart from the CURRENT NowPlaying — if playback advanced during the await,
    // OnTrackChanged already hydrated IsStarred for the new track; don't clobber it.
    private void RevertStar(PlaybackTrack track, bool wasStarred)
    {
        track.IsStarred = wasStarred;
        IsStarred = _playbackService.NowPlaying?.IsStarred ?? false;
    }
```

> **Why not a plain revert:** an earlier version set `IsStarred = wasStarred` directly in the `catch`. That clobbers the heart if the track changed during the await, and it silently ignored Subsonic `status="failed"` responses. Keep the status check and the `RevertStar` rehydration — both are covered by tests in Step 4 (`ToggleStar_ApiReturnsFailedStatus_Reverts`, `ToggleStar_TrackChangesDuringFailedRequest_DoesNotClobberNewTrack`).

- [ ] **Step 10: Update existing PlayerViewModel test construction**

The constructor signature changed, so the four existing test files that build `PlayerViewModel` must pass the two new arguments. In each file, add `using Hypersonic.Tests.Fakes;` (if absent) and update every `new PlayerViewModel(playback, appState, createTimer: false)` call to:

```csharp
new PlayerViewModel(playback, appState, new FakeSubsonicClientService(), new NullLoggingService(), createTimer: false)
```

Apply to:
- `PlayerViewModelToggleTests.cs` — the `CreateViewModel()` helper (line 87) and the inline construction in `Constructor_DoesNotPersistDuringHydration` (line 181).
- `PlayerViewModelMetadataTests.cs` — all three constructions (lines 91, 122, 148).
- `PlayerViewModelRefreshQueueTests.cs` — every `new PlayerViewModel(...)` call.
- `PlayerViewModelJumpToQueueIndexTests.cs` — every `new PlayerViewModel(...)` call.

If a file declares its own private nested `FakePlaybackService` (Toggle/Metadata tests do), keep it — only the `PlayerViewModel` construction args change. The shared `FakeSubsonicClientService` and `NullLoggingService` come from `Hypersonic.Tests.Fakes`; there is no name collision because these four files do not define their own versions.

- [ ] **Step 11: Run the favorite tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~PlayerViewModelFavoriteTests"`
Expected: PASS — the six hydration/toggle cases plus the three guard tests noted in Step 4 (`ToggleStar_ApiReturnsFailedStatus_Reverts`, `ToggleStar_TrackChangesDuringFailedRequest_DoesNotClobberNewTrack`, `NotifyThemeChanged_RaisesIsStarred`).

- [ ] **Step 12: Run the full app test project to confirm no regressions**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS (all tests, including the updated PlayerViewModel toggle/metadata/queue tests).

- [ ] **Step 13: Commit**

```bash
git add Hypersonic/ViewModels/PlayerViewModel.cs Hypersonic.Tests/Fakes/NullLoggingService.cs Hypersonic.Tests/Fakes/FakeSubsonicClientService.cs Hypersonic.Tests/Fakes/RecordingSubsonicClient.cs Hypersonic.Tests/ViewModels/PlayerViewModelFavoriteTests.cs Hypersonic.Tests/ViewModels/PlayerViewModelToggleTests.cs Hypersonic.Tests/ViewModels/PlayerViewModelMetadataTests.cs Hypersonic.Tests/ViewModels/PlayerViewModelRefreshQueueTests.cs Hypersonic.Tests/ViewModels/PlayerViewModelJumpToQueueIndexTests.cs
git commit -m "feat(player): add IsStarred state and ToggleStar command to PlayerViewModel"
```

---

## Task 4: Player bar heart button (XAML)

**Files:**
- Modify: `Hypersonic/Views/ShellPage.xaml` (`PlayerControlsGrid`, lines 505–579)

- [ ] **Step 1: Add a column for the heart button**

In `PlayerControlsGrid`'s `ColumnDefinitions` (lines 506–516), add one `Auto` column **before** the final `Auto` (the queue toggle's column). The grid goes from 9 to 10 columns:

```xml
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
```

- [ ] **Step 2: Insert the favorite button between duration text (col 7) and queue toggle**

Immediately after the duration `TextBlock` (`Grid.Column="7"`, lines 566–568) and before the `QueueToggleButton`, add the favorite button. It uses `TransparentButtonStyle` (see the note below) so the disabled (no-track) state stays transparent — **not** a plain `Button` with `Background="Transparent"`, whose default template paints `ButtonBackgroundDisabled` (a confusing fill box) when disabled:

```xml
                    <Button Grid.Column="8"
                            Command="{x:Bind Player.ToggleStarCommand}"
                            IsEnabled="{x:Bind Player.HasTrack, Mode=OneWay}"
                            Style="{StaticResource TransparentButtonStyle}"
                            Width="32" Height="32" Margin="8,0,0,0"
                            VerticalAlignment="Center"
                            ToolTipService.ToolTip="Favorite">
                        <FontIcon FontSize="14"
                                  Glyph="{x:Bind Player.IsStarred, Mode=OneWay,
                                          Converter={StaticResource BoolToGlyphConverter},
                                          ConverterParameter='&#xEB52;|&#xEB51;'}"
                                  Foreground="{x:Bind Player.IsStarred, Mode=OneWay,
                                          Converter={StaticResource BoolToFavoriteBrushConverter}}"/>
                    </Button>
```

> **`TransparentButtonStyle`** is a `Button` style added to `Themes/Generic.xaml` (mirrors the existing `TransparentToggleButtonStyle`): transparent background in Normal **and** Disabled, `ControlFillColorSecondaryBrush`/`Tertiary` fill on hover/press (matching the neighbouring transport/queue buttons), and a Disabled visual state that dims the content to `0.4` opacity. Opacity is used because it is the only template-level lever that overrides the `FontIcon`'s explicit `Foreground`.

- [ ] **Step 3: Move the queue toggle to column 9**

Change the `QueueToggleButton`'s `Grid.Column="8"` (line 570) to `Grid.Column="9"`. Leave everything else about it unchanged.

- [ ] **Step 4: Build to verify XAML compiles**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: Build succeeded (XAML compiler resolves both converters and the new bindings).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/Views/ShellPage.xaml
git commit -m "feat(player): add favorite heart button to player bar"
```

---

## Task 5: Full verification

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Hypersonic.sln -p:Platform=x64`
Expected: Build succeeded, no warnings introduced by these changes.

- [ ] **Step 2: Run all test projects**

Run:
```
dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj
dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64
dotnet test Hypersonic.Client.UnitTest/Hypersonic.Client.UnitTest.csproj
```
Expected: all PASS.

- [ ] **Step 3: Manual smoke test (F5 / deploy MSIX)**

The main app cannot run via `dotnet run`; launch from Visual Studio (F5) or deploy the MSIX. Verify against a live Subsonic/Navidrome server:

1. With nothing playing: the heart sits left of the queue button, shows a muted outline heart, and is disabled (not clickable).
2. Play a track that is **not** favorited → outline heart appears, enabled.
3. Click it → heart fills red (`#EB52`, `#E74856`); confirm in the server UI the track is now starred.
4. Click again → reverts to outline; confirm unstarred on the server.
5. Play a track that **is** already favorited (star it in a track list first) → the player bar shows a red filled heart immediately on track change, no flicker.
6. Skip to the next/previous track → heart updates to reflect each track's star state.

- [ ] **Step 4: Update CLAUDE.md converter list (docs)**

In `CLAUDE.md`, the "XAML converters" section lists the global converters. Add `BoolToFavoriteBrushConverter` to that list so the docs stay accurate.

```bash
git add CLAUDE.md
git commit -m "docs: note BoolToFavoriteBrushConverter in converter list"
```

---

## Self-review notes

- **Spec coverage:** Model field (Task 1), VM state + optimistic toggle + revert + hydration (Task 3), converter (Task 2), XAML placement/column shift/states (Task 4), tests for propagation + toggle + hydration (Tasks 1, 3), CLAUDE.md doc update (Task 5). All design sections map to a task.
- **No-track behavior:** `IsEnabled="{x:Bind Player.HasTrack}"` + `IsStarred` false → muted outline, disabled (matches the design's accepted nuance).
- **Type consistency:** `ToggleStarCommand` (generated from `ToggleStarAsync`), `IsStarred`, and the 5-arg constructor are referenced identically across Task 3's tests and implementation. `RecordingSubsonicClient` exposes `StarCallCount`/`UnstarCallCount`/`StarShouldThrow`/`LastStarredId` used by the tests.
- **Out of scope (unchanged):** cross-page star sync; NowPlayingPage; search-suggestion star state.
