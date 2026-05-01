using Hypersonic.Core.Models;
using Hypersonic.Core.Services;

namespace Hypersonic.Core.Tests.Services;

[TestClass]
public class PlaybackQueueJumpToQueueIndexTests
{
    private static List<PlaybackTrack> MakeTracks(int count)
    {
        var tracks = new List<PlaybackTrack>();
        for (var i = 0; i < count; i++)
        {
            tracks.Add(new PlaybackTrack
            {
                Id = $"track-{i}",
                Title = $"Track {i}",
            });
        }
        return tracks;
    }

    [TestMethod]
    public void Returns_Null_WhenIndexOutOfRange()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 0);

        Assert.IsNull(queue.JumpToQueueIndex(-1));
        Assert.IsNull(queue.JumpToQueueIndex(3));
        Assert.IsNull(queue.JumpToQueueIndex(99));
        Assert.AreEqual(0, queue.CurrentIndex);
    }

    [TestMethod]
    public void Returns_Null_WhenAlreadyCurrent()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 1);

        Assert.IsNull(queue.JumpToQueueIndex(1));
        Assert.AreEqual(1, queue.CurrentIndex);
    }

    [TestMethod]
    public void NoOp_DoesNotPushHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 0);

        queue.JumpToQueueIndex(0);
        queue.JumpToQueueIndex(99);
        Assert.IsFalse(queue.HasPrevious);
    }

    [TestMethod]
    public void Unshuffled_JumpsToTargetTrack()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);

        var result = queue.JumpToQueueIndex(3);

        Assert.IsNotNull(result);
        Assert.AreEqual("track-3", result!.Id);
        Assert.AreEqual(3, queue.CurrentIndex);
        Assert.AreEqual(3, queue.EffectiveIndex);
    }

    [TestMethod]
    public void Unshuffled_DuplicateTrack_JumpsToDuplicateOccurrence()
    {
        var tracks = MakeTracks(2);
        // Add a duplicate of track-0 at position 3.
        tracks.Add(new PlaybackTrack { Id = "track-spacer" });
        tracks.Add(new PlaybackTrack { Id = "track-0", Title = "Track 0 (dup)" });

        var queue = new PlaybackQueue();
        queue.SetQueue(tracks, 0);

        var result = queue.JumpToQueueIndex(3);

        Assert.IsNotNull(result);
        Assert.AreSame(tracks[3], result);
        Assert.AreEqual(3, queue.CurrentIndex);
    }

    [TestMethod]
    public void Shuffled_LandsOnTargetEffectiveIndex_AndAdvancesFromThere()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(10), 0);
        queue.SetShuffle(true);

        var effective = queue.GetEffectiveQueue();
        // Pick a non-terminal index.
        var target = 4;
        Assert.IsTrue(target < effective.Count - 1);
        var expectedTrack = effective[target];
        var expectedNext = effective[target + 1];

        var result = queue.JumpToQueueIndex(target);

        Assert.IsNotNull(result);
        Assert.AreSame(expectedTrack, result);
        Assert.AreEqual(target, queue.EffectiveIndex);

        var next = queue.AdvanceManual();
        Assert.IsNotNull(next);
        Assert.AreSame(expectedNext, next);
        Assert.AreEqual(target + 1, queue.EffectiveIndex);
    }

    [TestMethod]
    public void Shuffled_TerminalIndex_RepeatOff_NextAdvanceIsNoOp()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.SetShuffle(true);
        queue.RepeatMode = RepeatMode.Off;

        var effective = queue.GetEffectiveQueue();
        var terminal = effective.Count - 1;
        var expectedTrack = effective[terminal];

        var result = queue.JumpToQueueIndex(terminal);

        Assert.IsNotNull(result);
        Assert.AreSame(expectedTrack, result);
        Assert.AreEqual(terminal, queue.EffectiveIndex);

        var next = queue.AdvanceManual();
        Assert.IsNull(next);
        // Cursor must be unchanged.
        Assert.AreEqual(terminal, queue.EffectiveIndex);
        Assert.AreSame(expectedTrack, queue.CurrentTrack);
    }

    [TestMethod]
    public void Shuffled_TerminalIndex_RepeatAll_AdvanceWraps()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.SetShuffle(true);
        queue.RepeatMode = RepeatMode.All;

        var effective = queue.GetEffectiveQueue();
        var terminal = effective.Count - 1;
        queue.JumpToQueueIndex(terminal);

        var next = queue.AdvanceManual();
        Assert.IsNotNull(next);
        // After wrapping, _shufflePosition is reset to 0.
        Assert.AreEqual(0, queue.EffectiveIndex);
    }

    [TestMethod]
    public void Shuffled_DuplicateTrack_LandsOnDuplicateOccurrence()
    {
        var trackA = new PlaybackTrack { Id = "dup", Title = "A" };
        var trackB = new PlaybackTrack { Id = "dup", Title = "B" };
        var filler1 = new PlaybackTrack { Id = "f1" };
        var filler2 = new PlaybackTrack { Id = "f2" };
        var filler3 = new PlaybackTrack { Id = "f3" };

        var queue = new PlaybackQueue();
        queue.SetQueue(new List<PlaybackTrack> { trackA, filler1, filler2, filler3, trackB }, 0);
        queue.SetShuffle(true);

        var effective = queue.GetEffectiveQueue();
        var iB = -1;
        for (var i = 0; i < effective.Count; i++)
        {
            if (ReferenceEquals(effective[i], trackB))
            {
                iB = i;
                break;
            }
        }
        Assert.AreNotEqual(-1, iB);

        var result = queue.JumpToQueueIndex(iB);

        Assert.IsNotNull(result);
        Assert.AreSame(trackB, result);
        Assert.AreEqual(iB, queue.EffectiveIndex);
        Assert.AreSame(trackB, queue.CurrentTrack);
    }

    [TestMethod]
    public void SuccessfulJump_PushesOutgoingTrackToHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);

        queue.JumpToQueueIndex(2);

        Assert.IsTrue(queue.HasPrevious);
        var previous = queue.GoBack();
        Assert.IsNotNull(previous);
        Assert.AreEqual("track-0", previous!.Id);
    }
}
