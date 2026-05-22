namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// One timed word (or syllable) inside a <see cref="LyricsLine"/>.
/// </summary>
public class LyricsWord
{
    public int StartMs { get; init; }
    public int EndMs { get; init; }
    public string Text { get; init; } = string.Empty;

    public int DurationMs => Math.Max(EndMs - StartMs, 0);
}
