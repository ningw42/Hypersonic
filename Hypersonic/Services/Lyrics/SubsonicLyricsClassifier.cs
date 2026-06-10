using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Classifies a Subsonic <c>Status="failed"</c> response code into a
/// <see cref="LyricsOutcomeKind"/>. Relocated from the former <c>LyricsFetchPolicy</c>:
/// the semantics are Subsonic-specific and belong with the Subsonic source.
/// </summary>
internal static class SubsonicLyricsClassifier
{
    /// <summary>
    /// True iff <paramref name="code"/> is a "definitive" failure (20 protocol mismatch,
    /// 30 incompatible client, 70 data not found). All other codes are transient.
    /// </summary>
    public static bool IsDefinitiveErrorCode(int code) => code is 20 or 30 or 70;

    /// <summary>True iff <paramref name="code"/> means "data not found" (70) vs "endpoint unavailable" (20/30).</summary>
    public static bool IsEmptyClassificationCode(int code) => code == 70;

    /// <summary>
    /// Maps a failed-status code: definitive codes become empty (70) or unsupported (20/30);
    /// any other code is transient.
    /// </summary>
    public static LyricsOutcomeKind ClassifyFailedStatus(int code)
    {
        if (!IsDefinitiveErrorCode(code))
        {
            return LyricsOutcomeKind.Transient;
        }

        return IsEmptyClassificationCode(code)
            ? LyricsOutcomeKind.DefinitiveEmpty
            : LyricsOutcomeKind.DefinitiveUnsupported;
    }
}
