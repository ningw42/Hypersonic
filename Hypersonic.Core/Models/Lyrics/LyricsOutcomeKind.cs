namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// Per-source classification of a lyrics fetch outcome. Shared by the provider
/// contract (<see cref="Hypersonic.Core.Models.Lyrics.LyricsProviderResult"/>) and the
/// Subsonic error-code helpers. Moved into Core (from the former
/// <c>Hypersonic.Helpers.LyricsFetchPolicy</c>) so the Core provider result can reuse it.
/// </summary>
public enum LyricsOutcomeKind
{
    /// <summary>A usable lyrics document was produced.</summary>
    Content,
    /// <summary>Definitive empty: the source says "no lyrics for this track".</summary>
    DefinitiveEmpty,
    /// <summary>Definitive unsupported: the source exists but cannot service the request.</summary>
    DefinitiveUnsupported,
    /// <summary>Transient: a throw, network failure, or non-definitive failed status.</summary>
    Transient,
}
