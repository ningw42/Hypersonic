using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Contracts.Services;

/// <summary>
/// A <b>local override source</b> for lyrics — NOT a fan-out provider. It exposes a sideloaded lyrics
/// file (<c>%LOCALAPPDATA%\Hypersonic\LyricsOverride\&lt;songId&gt;.ttml</c>) that <see cref="ILyricsService"/>
/// consults <i>before</i> the cache read, so a present file takes hard precedence over the cache and
/// every network provider — bypassing the fan-out and writing nothing (delete the file to revert).
/// Returns <c>null</c> when no override exists, which is the overwhelmingly common case, so the check
/// must be a cheap existence test run only on track change.
/// </summary>
public interface ILocalLyricsOverrideSource
{
    /// <summary>
    /// Returns the parsed override document for <paramref name="songId"/> if a sideloaded file is
    /// present and parses, otherwise <c>null</c>.
    /// </summary>
    Task<LyricsDocument?> TryGetOverrideAsync(string songId, CancellationToken cancellationToken = default);
}
