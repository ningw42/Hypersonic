using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Contracts.Lyrics;

/// <summary>
/// Turns a raw payload (the byte-for-byte provider response) into a validated
/// <see cref="LyricsDocument"/>. The single path from <c>(raw bytes, format)</c> to a document,
/// called by the provider at fetch time and by the cache at read time so the two can never
/// diverge. Implementations run <see cref="LyricsValidator"/> on their output and return
/// <c>null</c> (logging once) when the document comes back with issues.
/// </summary>
public interface ILyricsPayloadParser
{
    /// <summary>The format tag this parser handles (e.g. <c>"subsonic-structured"</c>).</summary>
    string FormatTag { get; }

    /// <summary>Parses <paramref name="payload"/> into a validated document, or <c>null</c> when unusable.</summary>
    LyricsDocument? Parse(ReadOnlyMemory<byte> payload);
}
