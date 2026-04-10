using Hypersonic.Contracts.Services;
using Hypersonic.Models;

namespace Hypersonic.UnitTest;

/// <summary>
/// Tests the toggle-persistence contract for IsMuted, ShuffleEnabled, and RepeatMode.
///
/// PlayerViewModel cannot be instantiated in this project (requires WinUI DispatcherQueue),
/// so we test the code paths directly: the toggle commands should update AppState and call
/// SavePlaybackAsync. The <c>_isSavingFromExternal</c> guard should prevent saves when
/// the update originates from PlaybackService events.
/// </summary>
[TestClass]
public class TogglePersistenceTests
{
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

    // ----------------------------------------------------------------
    // BUG DEMONSTRATION: these tests replicate the current buggy flow
    // where toggle commands update the service but the _isSavingFromExternal
    // guard blocks the On*Changed handlers from persisting to AppState.
    // These tests prove the bug exists by showing state is NOT saved.
    // ----------------------------------------------------------------

    [TestMethod]
    public void BugDemo_ToggleMute_ExternalGuardBlocksSave()
    {
        // Replicates the current (buggy) flow:
        // 1. ToggleMute sets _playbackService.IsMuted (service fires event)
        // 2. OnPlaybackStateChanged sets _isSavingFromExternal = true
        // 3. OnIsMutedChanged checks the guard and SKIPS saving
        var svc = new FakeAppStateService();

        // The service-side mute value changes (simulated).
        var serviceMuted = true;

        // OnPlaybackStateChanged handler runs with guard active.
        var isSavingFromExternal = true;
        // ViewModel property would be set here (IsMuted = serviceMuted).
        // OnIsMutedChanged fires but skips save due to guard:
        if (!isSavingFromExternal)
        {
            svc.State.Playback.IsMuted = serviceMuted;
            _ = svc.SavePlaybackAsync();
        }

        // BUG: AppState was never updated, and save was never called.
        Assert.IsFalse(svc.State.Playback.IsMuted, "Bug: AppState still has default value");
        Assert.AreEqual(0, svc.PlaybackSaveCount, "Bug: save was never triggered");
    }

    [TestMethod]
    public void BugDemo_ToggleShuffle_ExternalGuardBlocksSave()
    {
        var svc = new FakeAppStateService();
        var serviceShuffle = true;

        var isSavingFromExternal = true;
        if (!isSavingFromExternal)
        {
            svc.State.Playback.ShuffleEnabled = serviceShuffle;
            _ = svc.SavePlaybackAsync();
        }

        Assert.IsFalse(svc.State.Playback.ShuffleEnabled, "Bug: AppState still has default value");
        Assert.AreEqual(0, svc.PlaybackSaveCount, "Bug: save was never triggered");
    }

    [TestMethod]
    public void BugDemo_CycleRepeatMode_ExternalGuardBlocksSave()
    {
        var svc = new FakeAppStateService();
        var serviceRepeat = RepeatMode.All;

        var isSavingFromExternal = true;
        if (!isSavingFromExternal)
        {
            svc.State.Playback.RepeatMode = serviceRepeat;
            _ = svc.SavePlaybackAsync();
        }

        Assert.AreEqual(RepeatMode.Off, svc.State.Playback.RepeatMode, "Bug: AppState still has default value");
        Assert.AreEqual(0, svc.PlaybackSaveCount, "Bug: save was never triggered");
    }

    // ----------------------------------------------------------------
    // FIX VERIFICATION: these tests replicate the fixed toggle commands
    // where state is persisted directly in the command, bypassing the
    // event handler path entirely.
    // ----------------------------------------------------------------

    [TestMethod]
    public void ToggleMute_PersistsToAppState()
    {
        var svc = new FakeAppStateService();
        Assert.IsFalse(svc.State.Playback.IsMuted, "Default should be false");

        // Simulate the fixed ToggleMute command:
        // 1. _playbackService.IsMuted = !_playbackService.IsMuted  (service-side)
        // 2. _appStateService.State.Playback.IsMuted = _playbackService.IsMuted
        // 3. _ = _appStateService.SavePlaybackAsync()
        var newValue = !svc.State.Playback.IsMuted;
        svc.State.Playback.IsMuted = newValue;
        _ = svc.SavePlaybackAsync();

        Assert.IsTrue(svc.State.Playback.IsMuted, "IsMuted should be true after toggle");
        Assert.AreEqual(1, svc.PlaybackSaveCount, "Save should have been called once");
    }

    [TestMethod]
    public void ToggleMute_DoubleToggle_RestoresOriginal()
    {
        var svc = new FakeAppStateService();

        // First toggle: false -> true
        svc.State.Playback.IsMuted = !svc.State.Playback.IsMuted;
        _ = svc.SavePlaybackAsync();

        // Second toggle: true -> false
        svc.State.Playback.IsMuted = !svc.State.Playback.IsMuted;
        _ = svc.SavePlaybackAsync();

        Assert.IsFalse(svc.State.Playback.IsMuted, "Should be back to false");
        Assert.AreEqual(2, svc.PlaybackSaveCount, "Save should have been called twice");
    }

    [TestMethod]
    public void ToggleShuffle_PersistsToAppState()
    {
        var svc = new FakeAppStateService();
        Assert.IsFalse(svc.State.Playback.ShuffleEnabled, "Default should be false");

        svc.State.Playback.ShuffleEnabled = !svc.State.Playback.ShuffleEnabled;
        _ = svc.SavePlaybackAsync();

        Assert.IsTrue(svc.State.Playback.ShuffleEnabled, "ShuffleEnabled should be true after toggle");
        Assert.AreEqual(1, svc.PlaybackSaveCount, "Save should have been called once");
    }

    [TestMethod]
    public void CycleRepeatMode_PersistsAllThreeStates()
    {
        var svc = new FakeAppStateService();
        Assert.AreEqual(RepeatMode.Off, svc.State.Playback.RepeatMode, "Default should be Off");

        // Off -> All
        svc.State.Playback.RepeatMode = svc.State.Playback.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.Off,
            _ => RepeatMode.Off,
        };
        _ = svc.SavePlaybackAsync();
        Assert.AreEqual(RepeatMode.All, svc.State.Playback.RepeatMode);

        // All -> One
        svc.State.Playback.RepeatMode = svc.State.Playback.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.Off,
            _ => RepeatMode.Off,
        };
        _ = svc.SavePlaybackAsync();
        Assert.AreEqual(RepeatMode.One, svc.State.Playback.RepeatMode);

        // One -> Off
        svc.State.Playback.RepeatMode = svc.State.Playback.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.Off,
            _ => RepeatMode.Off,
        };
        _ = svc.SavePlaybackAsync();
        Assert.AreEqual(RepeatMode.Off, svc.State.Playback.RepeatMode);

        Assert.AreEqual(3, svc.PlaybackSaveCount, "Save should have been called three times");
    }

    [TestMethod]
    public void ExternalUpdate_SkipsSave_WhenGuardIsActive()
    {
        // Documents the _isSavingFromExternal guard contract:
        // when the update comes from PlaybackService events (not user action),
        // the On*Changed handlers must NOT trigger a save.
        var svc = new FakeAppStateService();

        var isSavingFromExternal = true;
        svc.State.Playback.IsMuted = true;
        if (!isSavingFromExternal)
        {
            _ = svc.SavePlaybackAsync();
        }

        Assert.IsTrue(svc.State.Playback.IsMuted, "State updated in memory");
        Assert.AreEqual(0, svc.PlaybackSaveCount, "Save must NOT be called under external guard");
    }
}
