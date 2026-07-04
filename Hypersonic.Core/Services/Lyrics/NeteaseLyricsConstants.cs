namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Constants for the Netease lyrics source: its provider id, the YRC (word) format tag, and a
/// source-scoped LRC (line) format tag distinct from lrclib's so the parser stamps "netease".
/// </summary>
public static class NeteaseLyricsConstants
{
    public const string ProviderId = "netease";
    public const string YrcFormat = "yrc";
    public const string LrcFormat = "netease-lrc";
}
