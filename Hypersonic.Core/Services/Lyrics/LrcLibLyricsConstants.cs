namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Shared constants for the lrclib lyrics source — its provider id and the two payload format
/// tags. Lives in Core because the lrclib parsers (Core) and the provider (Hypersonic) both
/// reference it, so the tags have a single owner.
/// </summary>
public static class LrcLibLyricsConstants
{
    public const string ProviderId = "lrclib";
    public const string SyncedFormat = "lrclib-synced";
    public const string PlainFormat = "lrclib-plain";
}
