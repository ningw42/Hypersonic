using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>Which external platform to search/fetch through the Lyricify library.</summary>
public enum LyricifyPlatform
{
    QQMusic,
    Netease,
    Kugou,
}

/// <summary>The library's best raw answer for a query: decrypted lyric text, its payload format tag,
/// and the matched candidate's metadata (for the provider's confidence gate).</summary>
public sealed record LyricifyLyricsResult(string RawText, string FormatTag, LyricsCandidate Candidate);

/// <summary>
/// The single seam over <c>Lyricify.Lyrics.Helper</c>: search + fetch + decrypt for one platform.
/// Returns <c>null</c> when the search yields no candidate; throws on library/network failure (callers
/// map that to a transient outcome). Isolating the library here keeps the providers testable and bounds
/// the blast radius of a future library swap.
/// </summary>
public interface ILyricifyLyricsClient
{
    Task<LyricifyLyricsResult?> FetchAsync(LyricifyPlatform platform, LyricsQuery query, CancellationToken cancellationToken);
}
