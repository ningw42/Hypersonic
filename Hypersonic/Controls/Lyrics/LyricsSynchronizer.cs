namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// Resolves the active line index from the estimated position by a cached linear scan: the active line
/// is the last line whose start is at or before the position (so a line stays active through any gap
/// until the next begins). Remembering the last index makes forward playback O(1); a backward seek
/// re-scans from the front. Single-lane port of BetterLyrics' synchronizer.
/// </summary>
internal sealed class LyricsSynchronizer
{
    private int _last = -1;

    /// <summary>Returns the active line index, or <c>-1</c> before the first line (intro).</summary>
    public int Resolve(IReadOnlyList<LyricsTimingLine> lines, double positionMs)
    {
        var n = lines.Count;
        if (n == 0 || positionMs < lines[0].StartMs)
        {
            _last = -1;
            return -1;
        }

        // Resume from the cached index while still ahead of it (forward playback → O(1)); a backward
        // seek (position now before the cached line) restarts from the front.
        var i = _last >= 0 && _last < n && positionMs >= lines[_last].StartMs ? _last : 0;
        while (i + 1 < n && lines[i + 1].StartMs <= positionMs)
        {
            i++;
        }

        _last = i;
        return i;
    }

    /// <summary>Forgets the cached index — call on a new track / model rebuild.</summary>
    public void Reset() => _last = -1;
}
