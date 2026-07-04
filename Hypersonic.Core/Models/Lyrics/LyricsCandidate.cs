namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// A search result's identifying metadata, scored against a <see cref="LyricsQuery"/> by
/// <see cref="Hypersonic.Core.Services.Lyrics.LyricsMatchScorer"/>. Provider/library-agnostic so
/// the scorer stays pure and Core-testable.
/// </summary>
public sealed record LyricsCandidate(string? Title, string? Artist, string? Album, int? DurationSeconds);
