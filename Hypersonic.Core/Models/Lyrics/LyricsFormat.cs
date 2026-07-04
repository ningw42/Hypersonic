namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// The wire/file format of a lyrics payload — one member per genuinely-distinct parsing algorithm.
/// Orthogonal to <see cref="LyricsDocument.SourceProvider"/> (who served it) and
/// <see cref="LyricsSyncKind"/> (granularity). Also the parser-dispatch key in
/// <c>ILyricsParserRegistry</c>. <see cref="Unknown"/> is the default (negative-cache rows,
/// unparseable payloads).
/// </summary>
public enum LyricsFormat
{
    Unknown,
    PlainText,
    Lrc,
    Ttml,
    Qrc,
    Krc,
    Yrc,
    SubsonicStructured,
}
