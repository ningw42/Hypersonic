namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// Complete lyrics artifact for one track. Container for an ordered list of
/// <see cref="LyricsLine"/> plus metadata (sync granularity, language, source,
/// global offset).
/// </summary>
/// <remarks>
/// Named <c>LyricsDocument</c> rather than <c>Lyrics</c> to avoid colliding
/// with <c>Hypersonic.Client.Models.Entities.Lyrics</c> (the legacy Subsonic
/// DTO). See the Phase 0 design doc for rationale.
/// </remarks>
public class LyricsDocument
{
    private readonly IReadOnlyList<LyricsLine> _lines = Array.Empty<LyricsLine>();

    public LyricsSyncKind SyncKind { get; init; } = LyricsSyncKind.None;

    /// <summary>BCP-47 language code, when known (e.g. "en", "ja").</summary>
    public string? LanguageCode { get; init; }

    /// <summary>Identifier of the provider that produced this document (e.g. "subsonic", "lrclib").</summary>
    public string? SourceProvider { get; init; }

    /// <summary>Wire format of the payload this document was parsed from. Orthogonal to
    /// <see cref="SourceProvider"/>; stamped by the parser.</summary>
    public LyricsFormat Format { get; init; } = LyricsFormat.Unknown;

    /// <summary>Global offset in ms applied to every timestamp at render time. Negative = lyrics earlier than audio.</summary>
    public int? OffsetMs { get; init; }

    public IReadOnlyList<LyricsLine> Lines
    {
        get => _lines;
        init => _lines = value is null
            ? Array.Empty<LyricsLine>()
            : value.ToList().AsReadOnly();
    }

    /// <summary>Returns a copy with <see cref="SourceProvider"/> set; every other field is preserved.
    /// Used by providers to stamp identity onto a parser-produced document (init-only rebuild).</summary>
    public LyricsDocument WithSourceProvider(string sourceProvider) => new()
    {
        SyncKind = SyncKind,
        Format = Format,
        LanguageCode = LanguageCode,
        OffsetMs = OffsetMs,
        SourceProvider = sourceProvider,
        Lines = Lines,
    };

    /// <summary>Factory for the "definitively no lyrics" result, suitable for negative caching.</summary>
    public static LyricsDocument Empty(string? sourceProvider = null) => new()
    {
        SyncKind = LyricsSyncKind.None,
        SourceProvider = sourceProvider,
    };
}
