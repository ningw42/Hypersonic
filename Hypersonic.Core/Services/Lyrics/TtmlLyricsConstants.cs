namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Shared constants for the TTML lyrics format. Lives in Core because the parser (Core) and the
/// override source / future AMLL-TTML-DB provider (Hypersonic) both reference the format tag, so it
/// has a single owner. The tag also names the file extension the sideloaded override looks for.
/// </summary>
public static class TtmlLyricsConstants
{
    /// <summary>Format tag registered in the parser registry, and the override file extension (<c>.ttml</c>).</summary>
    public const string Format = "ttml";
}
