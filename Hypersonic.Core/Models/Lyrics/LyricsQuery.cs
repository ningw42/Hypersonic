namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// Everything any lyrics source might need to resolve a track's lyrics. Subsonic-structured
/// uses <see cref="SongId"/>; Subsonic-legacy uses <see cref="Artist"/> + <see cref="Title"/>;
/// external sources (lrclib, etc.) later use the rest. Carrying them all now means the query
/// shape does not change when Phase 1.5 lands.
/// </summary>
public sealed record LyricsQuery(
    string SongId,
    string? Artist = null,
    string? Title = null,
    string? Album = null,
    int? DurationSeconds = null);
