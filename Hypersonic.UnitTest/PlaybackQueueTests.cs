using Hypersonic.Models;
using Hypersonic.Services;

namespace Hypersonic.Tests.MSTest;

[TestClass]
public class PlaybackQueueTests
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
                Artist = "Artist",
                Album = "Album",
                Duration = 180,
                TrackNumber = i + 1,
            });
        }
        return tracks;
    }

    // ── SetQueue ────────────────────────────────────────────────────

    [TestMethod]
    public void SetQueue_SetsCurrentTrack()
    {
        var queue = new PlaybackQueue();
        var tracks = MakeTracks(5);

        queue.SetQueue(tracks, 2);

        Assert.AreEqual("track-2", queue.CurrentTrack!.Id);
        Assert.AreEqual(2, queue.CurrentIndex);
        Assert.AreEqual(5, queue.Tracks.Count);
    }

    [TestMethod]
    public void SetQueue_ClampsInvalidStartIndex()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 99);
        Assert.AreEqual("track-2", queue.CurrentTrack!.Id);

        queue.SetQueue(MakeTracks(3), -5);
        Assert.AreEqual("track-0", queue.CurrentTrack!.Id);
    }

    [TestMethod]
    public void SetQueue_EmptyTracks_SetsNoCurrentTrack()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(new List<PlaybackTrack>(), 0);
        Assert.IsNull(queue.CurrentTrack);
        Assert.AreEqual(-1, queue.CurrentIndex);
    }

    [TestMethod]
    public void SetQueue_ClearsHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.AdvanceManual(); // push history

        Assert.IsTrue(queue.HasPrevious);
        Assert.AreEqual(1, queue.History.Count);

        queue.SetQueue(MakeTracks(3), 0);

        Assert.IsFalse(queue.HasPrevious);
        Assert.AreEqual(0, queue.History.Count);
    }

    // ── AddNext ─────────────────────────────────────────────────────

    [TestMethod]
    public void AddNext_InsertsAfterCurrent()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 1); // playing track-1

        var newTracks = new List<PlaybackTrack>
        {
            new() { Id = "new-0", Title = "New 0" },
            new() { Id = "new-1", Title = "New 1" },
        };

        queue.AddNext(newTracks);

        Assert.AreEqual("track-1", queue.CurrentTrack!.Id);
        Assert.AreEqual(5, queue.Tracks.Count);

        // Advance should play the newly inserted track.
        var next = queue.AdvanceManual();
        Assert.AreEqual("new-0", next!.Id);

        next = queue.AdvanceManual();
        Assert.AreEqual("new-1", next!.Id);
    }

    [TestMethod]
    public void AddNext_WhenNothingPlaying_EstablishesCursor()
    {
        var queue = new PlaybackQueue();
        // No queue loaded yet — _currentIndex is -1.

        var newTracks = new List<PlaybackTrack>
        {
            new() { Id = "new-0", Title = "New 0" },
        };

        queue.AddNext(newTracks);

        Assert.AreEqual("new-0", queue.CurrentTrack!.Id);
        Assert.AreEqual(0, queue.CurrentIndex);
        Assert.AreEqual(1, queue.Tracks.Count);
    }

    // ── AddToEnd ────────────────────────────────────────────────────

    [TestMethod]
    public void AddToEnd_AppendsToQueue()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(2), 0);

        var newTracks = new List<PlaybackTrack>
        {
            new() { Id = "appended", Title = "Appended" },
        };

        queue.AddToEnd(newTracks);

        Assert.AreEqual(3, queue.Tracks.Count);
        Assert.AreEqual("appended", queue.Tracks[2].Id);
    }

    [TestMethod]
    public void AddToEnd_WhenNothingPlaying_EstablishesCursor()
    {
        var queue = new PlaybackQueue();

        queue.AddToEnd(new List<PlaybackTrack>
        {
            new() { Id = "new-0", Title = "New 0" },
        });

        Assert.AreEqual("new-0", queue.CurrentTrack!.Id);
        Assert.AreEqual(0, queue.CurrentIndex);
    }

    // ── RemoveAt ────────────────────────────────────────────────────

    [TestMethod]
    public void RemoveAt_CurrentTrack_AdvancesToNext()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 1); // playing track-1

        var newCurrent = queue.RemoveAt(1);

        Assert.IsNotNull(newCurrent);
        Assert.AreEqual("track-2", newCurrent!.Id);
        Assert.AreEqual("track-2", queue.CurrentTrack!.Id);
    }

    [TestMethod]
    public void RemoveAt_CurrentTrack_RecordsInHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 1);

        queue.RemoveAt(1); // remove current track

        // The removed track should appear in both play history and navigation history.
        Assert.AreEqual(1, queue.History.Count);
        Assert.AreEqual("track-1", queue.History[0].Id);
        Assert.IsTrue(queue.HasPrevious); // navigation history should have an entry
    }

    [TestMethod]
    public void RemoveAt_BeforeCurrent_AdjustsIndex()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 2); // playing track-2

        var result = queue.RemoveAt(0); // remove track-0

        Assert.IsNull(result);
        Assert.AreEqual("track-2", queue.CurrentTrack!.Id);
        Assert.AreEqual(1, queue.CurrentIndex);
        Assert.AreEqual(2, queue.Tracks.Count);
    }

    [TestMethod]
    public void RemoveAt_LastTrack_EmptiesQueue()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(1), 0);

        queue.RemoveAt(0);

        Assert.IsNull(queue.CurrentTrack);
        Assert.AreEqual(0, queue.Tracks.Count);
    }

    // ── MoveTrack ───────────────────────────────────────────────────

    [TestMethod]
    public void MoveTrack_ReordersUpcoming()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0); // playing track-0

        var moved = queue.MoveTrack(4, 1);

        Assert.IsTrue(moved);
        Assert.AreEqual("track-0", queue.CurrentTrack!.Id);
        Assert.AreEqual("track-4", queue.Tracks[1].Id);
        Assert.AreEqual("track-1", queue.Tracks[2].Id);
    }

    [TestMethod]
    public void MoveTrack_CurrentTrack_IsNoOp()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 1);

        var moved = queue.MoveTrack(1, 2);

        Assert.IsFalse(moved);
        Assert.AreEqual("track-1", queue.Tracks[1].Id);
    }

    [TestMethod]
    public void MoveTrack_WhileShuffled_IsNoOp()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.SetShuffle(true);

        var moved = queue.MoveTrack(2, 4);

        Assert.IsFalse(moved);
    }

    [TestMethod]
    public void MoveTrack_SameIndex_IsNoOp()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);

        var moved = queue.MoveTrack(2, 2);

        Assert.IsFalse(moved);
    }

    // ── ClearUpcoming ───────────────────────────────────────────────

    [TestMethod]
    public void ClearUpcoming_PreservesCurrent()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 2);

        queue.ClearUpcoming();

        Assert.AreEqual("track-2", queue.CurrentTrack!.Id);
        Assert.IsFalse(queue.HasNext);
        Assert.AreEqual(3, queue.Tracks.Count);
    }

    // ── ClearAll ────────────────────────────────────────────────────

    [TestMethod]
    public void ClearAll_ResetsEverything()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 2);
        queue.AdvanceManual(); // push history

        queue.ClearAll();

        Assert.IsNull(queue.CurrentTrack);
        Assert.AreEqual(0, queue.Tracks.Count);
        Assert.AreEqual(-1, queue.CurrentIndex);
        Assert.IsFalse(queue.HasNext);
        Assert.IsFalse(queue.HasPrevious);
        Assert.AreEqual(0, queue.History.Count);
    }

    // ── Shuffle ─────────────────────────────────────────────────────

    [TestMethod]
    public void ShuffleOn_CurrentTrackUnchanged()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(10), 3);

        queue.SetShuffle(true);

        Assert.AreEqual("track-3", queue.CurrentTrack!.Id);
    }

    [TestMethod]
    public void ShuffleOn_UpcomingRandomized()
    {
        var queue = new PlaybackQueue();
        var tracks = MakeTracks(20);
        var sawDifferentOrder = false;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            queue.SetShuffle(false);
            queue.SetQueue(new List<PlaybackTrack>(tracks), 0);
            queue.SetShuffle(true);

            var effective = queue.GetEffectiveQueue();
            var sequentialOrder = true;
            for (var i = 1; i < effective.Count; i++)
            {
                if (effective[i].Id != $"track-{i}")
                {
                    sequentialOrder = false;
                    break;
                }
            }

            if (!sequentialOrder)
            {
                sawDifferentOrder = true;
                break;
            }
        }

        Assert.IsTrue(sawDifferentOrder, "Shuffle should produce a different order at least once in 10 attempts");
    }

    [TestMethod]
    public void ShuffleOff_ResumesOriginalOrder()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 2);

        queue.SetShuffle(true);
        queue.AdvanceManual();
        queue.AdvanceManual();

        queue.SetShuffle(false);

        var current = queue.CurrentTrack!;
        Assert.IsNotNull(current);
        var currentIndex = queue.CurrentIndex;

        if (currentIndex < 4)
        {
            var next = queue.AdvanceManual();
            Assert.AreEqual($"track-{currentIndex + 1}", next!.Id);
        }
    }

    [TestMethod]
    public void SetShuffle_SameValue_IsIdempotent()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.SetShuffle(true);

        var effectiveBefore = queue.GetEffectiveQueue().Select(t => t.Id).ToList();

        // Setting shuffle true again should NOT reshuffle.
        queue.SetShuffle(true);

        var effectiveAfter = queue.GetEffectiveQueue().Select(t => t.Id).ToList();
        CollectionAssert.AreEqual(effectiveBefore, effectiveAfter);
    }

    [TestMethod]
    public void AddNext_WhileShuffled_PlaysNextCorrectly()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.SetShuffle(true);

        var insertedTrack = new PlaybackTrack { Id = "inserted", Title = "Inserted" };
        queue.AddNext(new List<PlaybackTrack> { insertedTrack });

        var next = queue.AdvanceManual();
        Assert.AreEqual("inserted", next!.Id);
    }

    [TestMethod]
    public void RemoveAt_WhileShuffled_IndicesConsistent()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.SetShuffle(true);

        queue.RemoveAt(3);

        Assert.AreEqual(4, queue.Tracks.Count);
        Assert.AreEqual("track-0", queue.CurrentTrack!.Id);

        var visited = new HashSet<string> { queue.CurrentTrack!.Id };
        for (var i = 0; i < 3; i++)
        {
            var next = queue.AdvanceManual();
            if (next == null)
            {
                break;
            }
            visited.Add(next.Id);
        }

        Assert.AreEqual(4, visited.Count);
    }

    // ── Repeat ──────────────────────────────────────────────────────

    [TestMethod]
    public void RepeatAll_WrapsAtEnd()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 2);
        queue.RepeatMode = RepeatMode.All;

        var next = queue.Advance();

        Assert.IsNotNull(next);
        Assert.AreEqual("track-0", next!.Id);
    }

    [TestMethod]
    public void RepeatAll_HasNextTrueAtEnd()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 2);
        queue.RepeatMode = RepeatMode.All;

        Assert.IsTrue(queue.HasNext);
    }

    [TestMethod]
    public void RepeatOne_MediaEnd_ReplaysCurrent()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 1);
        queue.RepeatMode = RepeatMode.One;

        var next = queue.Advance();

        Assert.AreEqual("track-1", next!.Id);
    }

    [TestMethod]
    public void RepeatOne_DoesNotPushHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 1);
        queue.RepeatMode = RepeatMode.One;

        queue.Advance();
        queue.Advance();
        queue.Advance();

        // RepeatOne auto-advance should not push history.
        Assert.IsFalse(queue.HasPrevious);
        Assert.AreEqual(0, queue.History.Count);
    }

    [TestMethod]
    public void RepeatOne_ManualNext_Advances()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 1);
        queue.RepeatMode = RepeatMode.One;

        var next = queue.AdvanceManual();

        Assert.AreEqual("track-2", next!.Id);
    }

    [TestMethod]
    public void RepeatOff_StopsAtEnd()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 2);
        queue.RepeatMode = RepeatMode.Off;

        var next = queue.Advance();

        Assert.IsNull(next);
        Assert.IsFalse(queue.HasNext);
    }

    // ── Advance does NOT push history when movement fails ───────────

    [TestMethod]
    public void AdvanceManual_AtEnd_DoesNotPushHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 2);
        queue.RepeatMode = RepeatMode.Off;

        var result = queue.AdvanceManual();

        Assert.IsNull(result);
        Assert.IsFalse(queue.HasPrevious);
        Assert.AreEqual(0, queue.History.Count);
    }

    [TestMethod]
    public void Advance_AtEnd_DoesNotPushHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 2);
        queue.RepeatMode = RepeatMode.Off;

        var result = queue.Advance();

        Assert.IsNull(result);
        Assert.IsFalse(queue.HasPrevious);
    }

    // ── Navigation History ──────────────────────────────────────────

    [TestMethod]
    public void PreviousTrack_ShuffleMode_ReversesPath()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.SetShuffle(true);

        var first = queue.CurrentTrack!.Id;
        queue.AdvanceManual();
        var second = queue.CurrentTrack!.Id;
        queue.AdvanceManual();

        // Go back: should reverse the actual path taken.
        var prev = queue.GoBack();
        Assert.AreEqual(second, prev!.Id);

        prev = queue.GoBack();
        Assert.AreEqual(first, prev!.Id);
    }

    [TestMethod]
    public void PreviousTrack_NavigationHistoryEmpty_ReturnsNull()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 0);

        var result = queue.GoBack();
        Assert.IsNull(result);
        Assert.IsFalse(queue.HasPrevious);
    }

    [TestMethod]
    public void PreviousTrack_AfterRemove_SkipsStaleEntry()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);

        queue.AdvanceManual(); // now at track-1, history: [track-0]
        queue.AdvanceManual(); // now at track-2, history: [track-0, track-1]

        // Remove track-1 (which is in navigation history).
        queue.RemoveAt(1); // track-1 removed

        // GoBack should skip the stale entry for removed track-1 and return to track-0.
        var prev = queue.GoBack();
        Assert.AreEqual("track-0", prev!.Id);
    }

    [TestMethod]
    public void PlayQueue_ClearsNavigationHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.AdvanceManual();

        Assert.IsTrue(queue.HasPrevious);

        queue.SetQueue(MakeTracks(3), 0);

        Assert.IsFalse(queue.HasPrevious);
    }

    [TestMethod]
    public void ClearAll_ClearsNavigationHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.AdvanceManual();

        Assert.IsTrue(queue.HasPrevious);

        queue.ClearAll();

        Assert.IsFalse(queue.HasPrevious);
    }

    [TestMethod]
    public void GoBack_RecordsToPlayHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);

        queue.AdvanceManual(); // history: 1 (track-0), play history: [track-0]
        queue.AdvanceManual(); // history: 2, play history: [track-0, track-1]

        var prevHistoryCount = queue.History.Count;
        queue.GoBack(); // going back should also add to play history

        Assert.AreEqual(prevHistoryCount + 1, queue.History.Count);
    }

    // ── EffectiveQueueIndex ─────────────────────────────────────────

    [TestMethod]
    public void EffectiveIndex_NoShuffle_EqualsCurrentIndex()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 2);

        Assert.AreEqual(queue.CurrentIndex, queue.EffectiveIndex);
    }

    [TestMethod]
    public void EffectiveIndex_Shuffle_ReturnsShufflePosition()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.SetShuffle(true);

        Assert.AreEqual(0, queue.EffectiveIndex);

        queue.AdvanceManual();
        Assert.AreEqual(1, queue.EffectiveIndex);
    }

    // ── History ring buffer ─────────────────────────────────────────

    [TestMethod]
    public void History_PopulatedOnTransitions()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);

        queue.AdvanceManual();
        queue.AdvanceManual();

        Assert.AreEqual(2, queue.History.Count);
        Assert.AreEqual("track-0", queue.History[0].Id);
        Assert.AreEqual("track-1", queue.History[1].Id);
    }

    // ── Navigation history uses track IDs, immune to index shifts ───

    [TestMethod]
    public void NavigationHistory_ImmuneToIndexShifts()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);

        // Play: track-0 → track-1 → track-2
        queue.AdvanceManual();
        queue.AdvanceManual();

        // Remove track-3 (a non-current, non-history track — just shifts indices)
        queue.RemoveAt(3);

        // Now at track-2 (still index 2). Go back should find track-1 by ID despite index shifts.
        var prev = queue.GoBack();
        Assert.AreEqual("track-1", prev!.Id);

        prev = queue.GoBack();
        Assert.AreEqual("track-0", prev!.Id);
    }

    // ── EffectiveIndex edge cases ───────────────────────────────────

    [TestMethod]
    public void EffectiveIndex_EmptyQueue_ReturnsMinusOne()
    {
        var queue = new PlaybackQueue();
        Assert.AreEqual(-1, queue.EffectiveIndex);
    }

    [TestMethod]
    public void EffectiveIndex_ShuffleOnEmptyQueue_ReturnsMinusOne()
    {
        var queue = new PlaybackQueue();
        queue.SetShuffle(true);
        Assert.AreEqual(-1, queue.EffectiveIndex);
    }

    // ── RemoveAt current track pushes to navigation history ─────────

    [TestMethod]
    public void RemoveAt_CurrentTrack_NavigationHistorySkipsRemovedTrack()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);

        // Advance to track-2: path is track-0 → track-1 → track-2
        queue.AdvanceManual();
        queue.AdvanceManual();

        // Remove current track (track-2). Advances to track-3.
        queue.RemoveAt(2);
        Assert.AreEqual("track-3", queue.CurrentTrack!.Id);

        // Navigation history has: [track-0, track-1, track-2 (from removal push)]
        // GoBack should skip track-2 (removed from queue) and land on track-1.
        var prev = queue.GoBack();
        Assert.IsNotNull(prev);
        Assert.AreEqual("track-1", prev!.Id);
    }

    // ── ClearAll clears play history via service path ────────────────

    [TestMethod]
    public void ClearAll_ClearsPlayHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);
        queue.AdvanceManual();
        queue.AdvanceManual();

        Assert.AreEqual(2, queue.History.Count);

        queue.ClearAll();

        Assert.AreEqual(0, queue.History.Count);
    }

    // ── GoBack appends to play history per design ───────────────────

    [TestMethod]
    public void GoBack_AppendsOutgoingToPlayHistory()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(5), 0);

        queue.AdvanceManual(); // at track-1, history: [track-0]
        queue.AdvanceManual(); // at track-2, history: [track-0, track-1]

        Assert.AreEqual(2, queue.History.Count);

        queue.GoBack(); // back to track-1, outgoing track-2 → play history

        Assert.AreEqual(3, queue.History.Count);
        Assert.AreEqual("track-2", queue.History[2].Id);
    }

    // ── Multiple AdvanceManual at end does NOT accumulate history ────

    [TestMethod]
    public void AdvanceManual_AtEnd_RepeatedCalls_NoHistoryAccumulation()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(MakeTracks(3), 2);
        queue.RepeatMode = RepeatMode.Off;

        queue.AdvanceManual();
        queue.AdvanceManual();
        queue.AdvanceManual();

        Assert.IsFalse(queue.HasPrevious);
        Assert.AreEqual(0, queue.History.Count);
    }

    // ── SetShuffle(false) on empty queue ────────────────────────────

    [TestMethod]
    public void SetShuffle_OffOnEmptyQueue_NoException()
    {
        var queue = new PlaybackQueue();
        queue.SetShuffle(true);
        queue.SetShuffle(false);

        Assert.AreEqual(-1, queue.EffectiveIndex);
        Assert.IsNull(queue.CurrentTrack);
    }
}
