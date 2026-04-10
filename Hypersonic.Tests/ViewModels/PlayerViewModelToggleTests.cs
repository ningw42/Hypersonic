using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hypersonic.Tests.ViewModels;

/// <summary>
/// Tests that toggle commands in PlayerViewModel persist state to AppState.
/// Uses the internal test constructor (createTimer: false) to avoid DispatcherQueue dependency.
/// </summary>
[TestClass]
public class PlayerViewModelToggleTests
{
    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class FakePlaybackService : IPlaybackService
    {
        public PlaybackTrack? NowPlaying => null;
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
        public event EventHandler? TrackChanged;
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
        public IReadOnlyList<PlaybackTrack> GetEffectiveQueue() => Array.Empty<PlaybackTrack>();

        public void RaisePlaybackStateChanged() => PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public event NavigatedEventHandler? Navigated;
        public bool CanGoBack => false;
        public Frame? Frame { get; set; }
        public bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false) => true;
        public bool GoBack() => false;
    }

    private sealed class FakeAppStateService : IAppStateService
    {
        public AppState State { get; } = new();
        public int PlaybackSaveCount { get; private set; }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;

        public Task SavePlaybackAsync()
        {
            PlaybackSaveCount++;
            return Task.CompletedTask;
        }

        public Task SaveWindowAsync() => Task.CompletedTask;
        public Task SaveNavigationAsync() => Task.CompletedTask;
        public void SaveSync() { }
    }

    private static (PlayerViewModel vm, FakePlaybackService playback, FakeAppStateService appState) CreateViewModel()
    {
        var playback = new FakePlaybackService();
        var nav = new FakeNavigationService();
        var appState = new FakeAppStateService();
        var vm = new PlayerViewModel(playback, nav, appState, createTimer: false);
        return (vm, playback, appState);
    }

    // ------------------------------------------------------------------
    // ToggleMute
    // ------------------------------------------------------------------

    [TestMethod]
    public void ToggleMuteCommand_PersistsIsMutedToAppState()
    {
        var (vm, playback, appState) = CreateViewModel();
        Assert.IsFalse(appState.State.Playback.IsMuted);

        vm.ToggleMuteCommand.Execute(null);

        Assert.IsTrue(playback.IsMuted, "PlaybackService.IsMuted should be toggled");
        Assert.IsTrue(appState.State.Playback.IsMuted, "AppState must reflect the new value");
        Assert.AreEqual(1, appState.PlaybackSaveCount, "SavePlaybackAsync must be called");
    }

    [TestMethod]
    public void ToggleMuteCommand_TwiceRestoresOriginal()
    {
        var (vm, _, appState) = CreateViewModel();

        vm.ToggleMuteCommand.Execute(null);
        vm.ToggleMuteCommand.Execute(null);

        Assert.IsFalse(appState.State.Playback.IsMuted, "Should be back to false after two toggles");
        Assert.AreEqual(2, appState.PlaybackSaveCount, "Two saves expected");
    }

    // ------------------------------------------------------------------
    // ToggleShuffle
    // ------------------------------------------------------------------

    [TestMethod]
    public void ToggleShuffleCommand_PersistsShuffleEnabledToAppState()
    {
        var (vm, playback, appState) = CreateViewModel();
        Assert.IsFalse(appState.State.Playback.ShuffleEnabled);

        vm.ToggleShuffleCommand.Execute(null);

        Assert.IsTrue(playback.ShuffleEnabled, "PlaybackService.ShuffleEnabled should be toggled");
        Assert.IsTrue(appState.State.Playback.ShuffleEnabled, "AppState must reflect the new value");
        Assert.AreEqual(1, appState.PlaybackSaveCount, "SavePlaybackAsync must be called");
    }

    // ------------------------------------------------------------------
    // CycleRepeatMode
    // ------------------------------------------------------------------

    [TestMethod]
    public void CycleRepeatModeCommand_PersistsEachModeToAppState()
    {
        var (vm, playback, appState) = CreateViewModel();
        Assert.AreEqual(RepeatMode.Off, appState.State.Playback.RepeatMode);

        // Off -> All
        vm.CycleRepeatModeCommand.Execute(null);
        Assert.AreEqual(RepeatMode.All, playback.RepeatMode);
        Assert.AreEqual(RepeatMode.All, appState.State.Playback.RepeatMode);

        // All -> One
        vm.CycleRepeatModeCommand.Execute(null);
        Assert.AreEqual(RepeatMode.One, playback.RepeatMode);
        Assert.AreEqual(RepeatMode.One, appState.State.Playback.RepeatMode);

        // One -> Off
        vm.CycleRepeatModeCommand.Execute(null);
        Assert.AreEqual(RepeatMode.Off, playback.RepeatMode);
        Assert.AreEqual(RepeatMode.Off, appState.State.Playback.RepeatMode);

        Assert.AreEqual(3, appState.PlaybackSaveCount, "Three saves expected (one per cycle step)");
    }

    // ------------------------------------------------------------------
    // Constructor hydration must NOT trigger saves
    // ------------------------------------------------------------------

    [TestMethod]
    public void Constructor_DoesNotPersistDuringHydration()
    {
        var playback = new FakePlaybackService
        {
            IsMuted = true,
            ShuffleEnabled = true,
            RepeatMode = RepeatMode.One,
            Volume = 0.75,
        };
        var appState = new FakeAppStateService();
        var nav = new FakeNavigationService();

        _ = new PlayerViewModel(playback, nav, appState, createTimer: false);

        Assert.AreEqual(0, appState.PlaybackSaveCount, "Hydration must not trigger saves");
    }
}
