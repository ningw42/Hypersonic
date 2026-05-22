namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// Granularity of the timing information in a <see cref="LyricsDocument"/>.
/// </summary>
public enum LyricsSyncKind
{
    /// <summary>Every line carries word-level timing. Karaoke-grade.</summary>
    Word,
    /// <summary>Lines carry start timestamps; no word-level timing.</summary>
    Line,
    /// <summary>Plain text lyrics with no timing information.</summary>
    Plain,
    /// <summary>Empty document — definitively no lyrics available for the track.</summary>
    None,
}
