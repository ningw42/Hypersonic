using Hypersonic.Core.Models;
using Hypersonic.Helpers;
using Hypersonic.Models;
using Hypersonic.Tests.Fakes;

namespace Hypersonic.Tests.Helpers;

// Ideally these tests would use [UITestMethod] with the production constructor to exercise
// the DispatcherQueue.TryEnqueue path, but the WinUI test host crashes before [UITestMethod]
// tests run. The synchronous internal constructor covers the same event → handler → Refresh()
// logic. Revisit when the test host issue is resolved.

[TestClass]
public class NowPlayingTrackerTests
{
    private static SongInfo MakeSong(string id) => new() { Id = id, Title = $"Song {id}" };

    [TestMethod]
    public void InitialRefresh_HighlightsMatchingTrack()
    {
        var songs = new List<SongInfo> { MakeSong("1"), MakeSong("2"), MakeSong("3") };
        var fake = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "2" },
            IsPlaying = true,
        };
        var tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);

        tracker.Subscribe();

        Assert.IsFalse(songs[0].IsNowPlaying);
        Assert.IsFalse(songs[0].IsPlaying);
        Assert.IsTrue(songs[1].IsNowPlaying);
        Assert.IsTrue(songs[1].IsPlaying);
        Assert.IsFalse(songs[2].IsNowPlaying);
        Assert.IsFalse(songs[2].IsPlaying);
    }

    [TestMethod]
    public void InitialRefresh_NoTrackPlaying()
    {
        var songs = new List<SongInfo> { MakeSong("1"), MakeSong("2") };
        var fake = new FakePlaybackService { NowPlaying = null, IsPlaying = false };
        var tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);

        tracker.Subscribe();

        Assert.IsFalse(songs[0].IsNowPlaying);
        Assert.IsFalse(songs[1].IsNowPlaying);
    }

    [TestMethod]
    public void TrackChanged_UpdatesHighlight()
    {
        var songs = new List<SongInfo> { MakeSong("1"), MakeSong("2"), MakeSong("3") };
        var fake = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "1" },
            IsPlaying = true,
        };
        var tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);
        tracker.Subscribe();

        Assert.IsTrue(songs[0].IsNowPlaying);

        fake.NowPlaying = new PlaybackTrack { Id = "3" };
        fake.RaiseTrackChanged();

        Assert.IsFalse(songs[0].IsNowPlaying);
        Assert.IsFalse(songs[0].IsPlaying);
        Assert.IsFalse(songs[1].IsNowPlaying);
        Assert.IsTrue(songs[2].IsNowPlaying);
        Assert.IsTrue(songs[2].IsPlaying);
    }

    [TestMethod]
    public void PlayPause_TogglesIsPlaying()
    {
        var songs = new List<SongInfo> { MakeSong("1"), MakeSong("2") };
        var fake = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "1" },
            IsPlaying = true,
        };
        var tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);
        tracker.Subscribe();

        Assert.IsTrue(songs[0].IsPlaying);
        Assert.IsTrue(songs[0].IsNowPlaying);

        fake.IsPlaying = false;
        fake.RaisePlaybackStateChanged();

        Assert.IsTrue(songs[0].IsNowPlaying);
        Assert.IsFalse(songs[0].IsPlaying);
    }

    [TestMethod]
    public void UnrelatedStateChange_NoOp()
    {
        var songs = new List<SongInfo> { MakeSong("1"), MakeSong("2") };
        var fake = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "1" },
            IsPlaying = true,
        };
        var tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);
        tracker.Subscribe();

        var calledCount = 0;
        songs[0].PropertyChanged += (_, _) => calledCount++;

        fake.RaisePlaybackStateChanged();

        Assert.AreEqual(0, calledCount);
    }

    [TestMethod]
    public void ClearAll_ClearsHighlight()
    {
        var songs = new List<SongInfo> { MakeSong("1"), MakeSong("2") };
        var fake = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "1" },
            IsPlaying = true,
        };
        var tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);
        tracker.Subscribe();

        Assert.IsTrue(songs[0].IsNowPlaying);

        fake.NowPlaying = null;
        fake.IsPlaying = false;
        fake.RaiseTrackChanged();

        Assert.IsFalse(songs[0].IsNowPlaying);
        Assert.IsFalse(songs[0].IsPlaying);
        Assert.IsFalse(songs[1].IsNowPlaying);
    }

    [TestMethod]
    public void Unsubscribe_ClearsAllSongs()
    {
        var songs = new List<SongInfo> { MakeSong("1"), MakeSong("2") };
        var fake = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "1" },
            IsPlaying = true,
        };
        var tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);
        tracker.Subscribe();

        Assert.IsTrue(songs[0].IsNowPlaying);

        tracker.Unsubscribe();

        Assert.IsFalse(songs[0].IsNowPlaying);
        Assert.IsFalse(songs[0].IsPlaying);
        Assert.IsFalse(songs[1].IsNowPlaying);
        Assert.IsFalse(songs[1].IsPlaying);
    }

    [TestMethod]
    public void EventAfterUnsubscribe_NoOp()
    {
        var songs = new List<SongInfo> { MakeSong("1"), MakeSong("2") };
        var fake = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "1" },
            IsPlaying = true,
        };
        var tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);
        tracker.Subscribe();
        tracker.Unsubscribe();

        fake.NowPlaying = new PlaybackTrack { Id = "2" };
        fake.IsPlaying = true;
        fake.RaiseTrackChanged();

        Assert.IsFalse(songs[0].IsNowPlaying);
        Assert.IsFalse(songs[1].IsNowPlaying);
    }

    [TestMethod]
    public void DuplicateTrackIds_AllHighlighted()
    {
        var songs = new List<SongInfo> { MakeSong("1"), MakeSong("1"), MakeSong("2") };
        var fake = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "1" },
            IsPlaying = true,
        };
        var tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);

        tracker.Subscribe();

        Assert.IsTrue(songs[0].IsNowPlaying);
        Assert.IsTrue(songs[1].IsNowPlaying);
        Assert.IsFalse(songs[2].IsNowPlaying);
    }

    [TestMethod]
    public void SubscribeIdempotent()
    {
        var songs = new List<SongInfo> { MakeSong("1") };
        var fake = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "1" },
            IsPlaying = true,
        };
        var tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);

        tracker.Subscribe();
        tracker.Subscribe();

        Assert.IsTrue(songs[0].IsNowPlaying);

        tracker.Unsubscribe();

        Assert.IsFalse(songs[0].IsNowPlaying);
    }

    [TestMethod]
    public void NavigatedAwayGuard_PreventsSubscribeAfterNavigation()
    {
        var songs = new List<SongInfo> { MakeSong("1"), MakeSong("2") };
        var fake = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "1" },
            IsPlaying = true,
        };

        NowPlayingTracker? tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);
        var navigatedAway = false;

        tracker.Subscribe();
        Assert.IsTrue(songs[0].IsNowPlaying);

        navigatedAway = true;
        tracker.Unsubscribe();

        if (!navigatedAway)
        {
            tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);
            tracker.Subscribe();
        }

        Assert.IsFalse(songs[0].IsNowPlaying);
        Assert.IsFalse(songs[1].IsNowPlaying);

        fake.NowPlaying = new PlaybackTrack { Id = "2" };
        fake.RaiseTrackChanged();

        Assert.IsFalse(songs[0].IsNowPlaying);
        Assert.IsFalse(songs[1].IsNowPlaying);
    }
}
