using Hypersonic.Models;

namespace Hypersonic.Services;

/// <summary>
/// Pure queue logic for playback — shuffle, repeat, navigation history, queue manipulation.
/// No dependency on MediaPlayer, DispatcherQueue, or WinUI. Fully unit-testable.
/// </summary>
public class PlaybackQueue
{
    private readonly List<PlaybackTrack> _tracks = [];
    private int _currentIndex = -1;

    // Shuffle
    private bool _shuffleEnabled;
    private readonly List<int> _shuffleOrder = [];
    private int _shufflePosition;
    private readonly Random _random = new();

    // Repeat
    private RepeatMode _repeatMode = RepeatMode.Off;

    // Play history (public, ring buffer for "recently played" display)
    private readonly List<PlaybackTrack> _history = [];
    private const int MaxHistorySize = 100;

    // Navigation history (internal, stores track IDs for PreviousTrack)
    // Uses track IDs instead of indices to be immune to index shifts from mutations.
    private readonly Stack<string> _navigationHistory = new();

    public PlaybackTrack? CurrentTrack =>
        _currentIndex >= 0 && _currentIndex < _tracks.Count ? _tracks[_currentIndex] : null;

    public IReadOnlyList<PlaybackTrack> Tracks => _tracks;

    public int CurrentIndex => _currentIndex;

    public int EffectiveIndex =>
        _currentIndex < 0 ? -1 : (_shuffleEnabled ? _shufflePosition : _currentIndex);

    public bool ShuffleEnabled => _shuffleEnabled;

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set => _repeatMode = value;
    }

    public IReadOnlyList<PlaybackTrack> History => _history;

    public bool HasNext
    {
        get
        {
            if (_tracks.Count == 0)
            {
                return false;
            }

            if (_repeatMode == RepeatMode.All)
            {
                return true;
            }

            // RepeatOne: HasNext behaves as Off (manual Next still advances)
            if (_shuffleEnabled)
            {
                return _shufflePosition < _shuffleOrder.Count - 1;
            }

            return _currentIndex < _tracks.Count - 1;
        }
    }

    public bool HasPrevious => _navigationHistory.Count > 0;

    public void SetQueue(IList<PlaybackTrack> tracks, int startIndex)
    {
        _tracks.Clear();
        _tracks.AddRange(tracks);
        _currentIndex = Math.Clamp(startIndex, 0, Math.Max(0, _tracks.Count - 1));
        if (_tracks.Count == 0)
        {
            _currentIndex = -1;
        }

        _navigationHistory.Clear();
        _history.Clear();

        if (_shuffleEnabled)
        {
            RebuildShuffleOrder();
        }
    }

    public void AddNext(IList<PlaybackTrack> tracks)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        // If nothing is playing, establish queue with cursor at 0 but do not auto-play.
        if (_currentIndex < 0 || _tracks.Count == 0)
        {
            _tracks.AddRange(tracks);
            _currentIndex = 0;
            if (_shuffleEnabled)
            {
                RebuildShuffleOrder();
            }
            return;
        }

        if (_shuffleEnabled)
        {
            var insertBase = _tracks.Count;
            _tracks.AddRange(tracks);

            // Insert new indices into _shuffleOrder at _shufflePosition + 1, preserving order.
            var insertAt = _shufflePosition + 1;
            for (var i = 0; i < tracks.Count; i++)
            {
                _shuffleOrder.Insert(insertAt + i, insertBase + i);
            }
        }
        else
        {
            // Insert after current in original order.
            var insertAt = _currentIndex + 1;
            for (var i = 0; i < tracks.Count; i++)
            {
                _tracks.Insert(insertAt + i, tracks[i]);
            }
            // No need to adjust _currentIndex — insertion is after it.
        }
    }

    public void AddToEnd(IList<PlaybackTrack> tracks)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        // If nothing is playing, establish queue with cursor at 0 but do not auto-play.
        if (_currentIndex < 0 || _tracks.Count == 0)
        {
            _tracks.AddRange(tracks);
            _currentIndex = 0;
            if (_shuffleEnabled)
            {
                RebuildShuffleOrder();
            }
            return;
        }

        var insertBase = _tracks.Count;
        _tracks.AddRange(tracks);

        if (_shuffleEnabled)
        {
            // Insert new indices at random positions after _shufflePosition in _shuffleOrder.
            for (var i = 0; i < tracks.Count; i++)
            {
                var pos = _random.Next(_shufflePosition + 1, _shuffleOrder.Count + 1);
                _shuffleOrder.Insert(pos, insertBase + i);
            }
        }
    }

    /// <summary>
    /// Remove a track by queue index.
    /// Returns the new current track if the current track was removed (caller should start playing it),
    /// or null if a non-current track was removed or the queue is now empty.
    /// </summary>
    public PlaybackTrack? RemoveAt(int index)
    {
        if (index < 0 || index >= _tracks.Count)
        {
            return null;
        }

        var wasCurrentTrack = index == _currentIndex;

        // Record the outgoing track in both histories if it was the current track.
        if (wasCurrentTrack)
        {
            PushCurrentToHistory();
        }

        _tracks.RemoveAt(index);

        if (_shuffleEnabled)
        {
            // Find and remove the index from _shuffleOrder.
            var shuffleIdx = _shuffleOrder.IndexOf(index);
            if (shuffleIdx >= 0)
            {
                _shuffleOrder.RemoveAt(shuffleIdx);
                if (shuffleIdx < _shufflePosition)
                {
                    _shufflePosition--;
                }
                else if (shuffleIdx == _shufflePosition && wasCurrentTrack)
                {
                    // Current track removed; _shufflePosition now points to the next track.
                    if (_shufflePosition >= _shuffleOrder.Count)
                    {
                        _shufflePosition = 0;
                    }
                }
            }

            // Decrement all entries > index to account for the shift in _tracks.
            for (var i = 0; i < _shuffleOrder.Count; i++)
            {
                if (_shuffleOrder[i] > index)
                {
                    _shuffleOrder[i]--;
                }
            }
        }

        // Adjust _currentIndex.
        if (wasCurrentTrack)
        {
            if (_tracks.Count == 0)
            {
                _currentIndex = -1;
            }
            else if (_shuffleEnabled)
            {
                _currentIndex = _shuffleOrder.Count > 0 && _shufflePosition < _shuffleOrder.Count
                    ? _shuffleOrder[_shufflePosition]
                    : -1;
            }
            else if (_currentIndex >= _tracks.Count)
            {
                _currentIndex = _tracks.Count > 0 ? _tracks.Count - 1 : -1;
            }
            // else: _currentIndex already points to the next track (shifted into position).

            return CurrentTrack;
        }
        else
        {
            if (index < _currentIndex)
            {
                _currentIndex--;
            }

            return null;
        }
    }

    /// <summary>
    /// Reorder a track within the queue. No-op while shuffled, for invalid indices,
    /// same-index moves, or attempts to move the current track.
    /// Returns true if the move actually happened.
    /// </summary>
    public bool MoveTrack(int fromIndex, int toIndex)
    {
        // No-op while shuffled.
        if (_shuffleEnabled)
        {
            return false;
        }

        if (fromIndex < 0 || fromIndex >= _tracks.Count ||
            toIndex < 0 || toIndex >= _tracks.Count ||
            fromIndex == toIndex ||
            fromIndex == _currentIndex)
        {
            return false;
        }

        var track = _tracks[fromIndex];
        _tracks.RemoveAt(fromIndex);
        _tracks.Insert(toIndex, track);

        // Adjust _currentIndex to follow the current track.
        if (fromIndex < _currentIndex && toIndex >= _currentIndex)
        {
            _currentIndex--;
        }
        else if (fromIndex > _currentIndex && toIndex <= _currentIndex)
        {
            _currentIndex++;
        }

        return true;
    }

    public void ClearUpcoming()
    {
        if (_shuffleEnabled)
        {
            // Collect unplayed track indices (after _shufflePosition).
            var unplayedIndices = new HashSet<int>();
            for (var i = _shufflePosition + 1; i < _shuffleOrder.Count; i++)
            {
                unplayedIndices.Add(_shuffleOrder[i]);
            }

            // Truncate shuffle order.
            if (_shufflePosition + 1 < _shuffleOrder.Count)
            {
                _shuffleOrder.RemoveRange(_shufflePosition + 1, _shuffleOrder.Count - _shufflePosition - 1);
            }

            // Remove unplayed tracks from _tracks in reverse order to avoid index shifting.
            var sortedIndices = unplayedIndices.OrderByDescending(x => x).ToList();
            foreach (var idx in sortedIndices)
            {
                _tracks.RemoveAt(idx);
            }

            // Reindex _shuffleOrder entries.
            for (var i = 0; i < _shuffleOrder.Count; i++)
            {
                var originalIdx = _shuffleOrder[i];
                var shiftCount = sortedIndices.Count(removed => removed < originalIdx);
                _shuffleOrder[i] = originalIdx - shiftCount;
            }

            // Update _currentIndex.
            _currentIndex = _shuffleOrder.Count > 0 && _shufflePosition < _shuffleOrder.Count
                ? _shuffleOrder[_shufflePosition]
                : -1;
        }
        else
        {
            if (_currentIndex >= 0 && _currentIndex < _tracks.Count - 1)
            {
                _tracks.RemoveRange(_currentIndex + 1, _tracks.Count - _currentIndex - 1);
            }
        }
    }

    public void ClearAll()
    {
        _tracks.Clear();
        _currentIndex = -1;
        _shuffleOrder.Clear();
        _shufflePosition = 0;
        _navigationHistory.Clear();
        _history.Clear();
    }

    /// <summary>
    /// Advance to the next track (auto-advance on media end).
    /// RepeatOne replays the current track without pushing history.
    /// Returns the new current track, or null if at end (repeat off).
    /// </summary>
    public PlaybackTrack? Advance()
    {
        if (_tracks.Count == 0)
        {
            return null;
        }

        // RepeatOne: return the same track without pushing history.
        if (_repeatMode == RepeatMode.One)
        {
            return CurrentTrack;
        }

        return AdvanceInternal();
    }

    /// <summary>
    /// Advance manually (for NextTrack button). Unlike Advance(), this always
    /// moves forward even in RepeatOne mode.
    /// Returns the new current track, or null if at end (repeat off).
    /// Does NOT push history or move cursor if movement is impossible.
    /// </summary>
    public PlaybackTrack? AdvanceManual()
    {
        if (_tracks.Count == 0)
        {
            return null;
        }

        return AdvanceInternal();
    }

    /// <summary>
    /// Go back to the previous track via navigation history.
    /// Records the outgoing track in play history.
    /// Returns the track to play, or null if history is empty.
    /// </summary>
    public PlaybackTrack? GoBack()
    {
        while (_navigationHistory.Count > 0)
        {
            var previousTrackId = _navigationHistory.Pop();

            // Find the track by ID — immune to index shifts.
            var foundIndex = _tracks.FindIndex(t => t.Id == previousTrackId);
            if (foundIndex >= 0)
            {
                // Record outgoing track in play history.
                PushToPlayHistory(CurrentTrack);

                _currentIndex = foundIndex;

                // Update shuffle position if shuffled.
                if (_shuffleEnabled)
                {
                    var pos = _shuffleOrder.IndexOf(_currentIndex);
                    if (pos >= 0)
                    {
                        _shufflePosition = pos;
                    }
                }

                return CurrentTrack;
            }
            // Track was removed — skip stale entry and try next.
        }

        return null;
    }

    public void SetShuffle(bool enabled)
    {
        if (_shuffleEnabled == enabled)
        {
            return; // Idempotent: no-op if already in the requested state.
        }

        _shuffleEnabled = enabled;

        if (enabled)
        {
            RebuildShuffleOrder();
        }
        else
        {
            _shuffleOrder.Clear();
            _shufflePosition = _currentIndex < 0 ? -1 : 0;
        }
    }

    /// <summary>
    /// Returns the queue in effective playback order.
    /// </summary>
    public IReadOnlyList<PlaybackTrack> GetEffectiveQueue()
    {
        if (!_shuffleEnabled)
        {
            return _tracks;
        }

        var result = new List<PlaybackTrack>(_shuffleOrder.Count);
        foreach (var idx in _shuffleOrder)
        {
            if (idx >= 0 && idx < _tracks.Count)
            {
                result.Add(_tracks[idx]);
            }
        }

        return result;
    }

    private PlaybackTrack? AdvanceInternal()
    {
        // Check if movement is possible BEFORE mutating state.
        bool canAdvance;
        if (_shuffleEnabled)
        {
            canAdvance = _shufflePosition < _shuffleOrder.Count - 1 || _repeatMode == RepeatMode.All;
        }
        else
        {
            canAdvance = _currentIndex < _tracks.Count - 1 || _repeatMode == RepeatMode.All;
        }

        if (!canAdvance)
        {
            return null;
        }

        // Movement is confirmed — now push history and move cursor.
        PushCurrentToHistory();

        if (_shuffleEnabled)
        {
            _shufflePosition++;
            if (_shufflePosition >= _shuffleOrder.Count)
            {
                // RepeatAll: reshuffle and wrap.
                RebuildShuffleOrder();
                _shufflePosition = 0;
            }

            _currentIndex = _shuffleOrder[_shufflePosition];
        }
        else
        {
            _currentIndex++;
            if (_currentIndex >= _tracks.Count)
            {
                // RepeatAll: wrap.
                _currentIndex = 0;
            }
        }

        return CurrentTrack;
    }

    private void RebuildShuffleOrder()
    {
        _shuffleOrder.Clear();

        if (_currentIndex < 0 || _tracks.Count == 0)
        {
            _shufflePosition = 0;
            return;
        }

        // Current track first.
        _shuffleOrder.Add(_currentIndex);

        // All other indices shuffled.
        var others = new List<int>();
        for (var i = 0; i < _tracks.Count; i++)
        {
            if (i != _currentIndex)
            {
                others.Add(i);
            }
        }

        // Fisher-Yates shuffle.
        for (var i = others.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (others[i], others[j]) = (others[j], others[i]);
        }

        _shuffleOrder.AddRange(others);
        _shufflePosition = 0;
    }

    private void PushCurrentToHistory()
    {
        var current = CurrentTrack;
        if (current == null)
        {
            return;
        }

        _navigationHistory.Push(current.Id);
        PushToPlayHistory(current);
    }

    private void PushToPlayHistory(PlaybackTrack? track)
    {
        if (track == null)
        {
            return;
        }

        _history.Add(track);
        if (_history.Count > MaxHistorySize)
        {
            _history.RemoveAt(0);
        }
    }
}
