using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Contracts.Lyrics;

/// <summary>
/// Resolves a format tag to its <see cref="ILyricsPayloadParser"/> and parses with it. Shared
/// by the provider (fetch time) and the cache (read time) so the raw bytes always travel the
/// same path to a <see cref="LyricsDocument"/>. Populated by DI from the registered parsers.
/// </summary>
public interface ILyricsParserRegistry
{
    /// <summary>
    /// Parses <paramref name="payload"/> with the parser registered for <paramref name="formatTag"/>.
    /// Returns <c>null</c> when no parser matches the tag or the parser rejects the payload.
    /// </summary>
    LyricsDocument? Parse(string formatTag, ReadOnlyMemory<byte> payload);
}
