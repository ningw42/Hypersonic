using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>The cache action the service should take for a fallback decision.</summary>
public enum LyricsCacheAction
{
    /// <summary>Write a positive entry from the winning content.</summary>
    WritePositive,
    /// <summary>Write a negative ("definitively no lyrics") entry.</summary>
    WriteNegative,
    /// <summary>Write nothing (non-definitive content, or a transient outcome).</summary>
    None,
}

/// <summary>
/// The outcome of the cross-provider fallback policy: the winning provider result (if any) and the
/// cache action the service should take.
/// </summary>
public readonly record struct LyricsFallbackDecision(LyricsProviderResult? Winner, LyricsCacheAction Action);

/// <summary>
/// Pure, concurrency-agnostic cross-provider policy, sitting next to the providers. Phase 1 feeds it
/// the results gathered by a sequential walk (short-circuited on the first content); Phase 1.5 will
/// feed it concurrent fan-out results — the contract does not change. Prefers the higher sync tier
/// (<c>Word &gt; Line &gt; Plain</c>) with registration order as the tiebreak; positive caching is
/// gated by the winner's <see cref="LyricsProviderResult.Definitive"/> bit; the negative cache is
/// written only when every result was definitive (any transient blocks it).
/// </summary>
public static class LyricsFallbackPolicy
{
    public static LyricsFallbackDecision Decide(IReadOnlyList<LyricsProviderResult> results)
    {
        LyricsProviderResult? winner = null;
        foreach (var result in results)
        {
            if (result.Kind != LyricsOutcomeKind.Content)
            {
                continue;
            }

            // Strict '>' keeps the earliest result on a tie — registration order is the tiebreak.
            if (winner is null || LyricsSyncKindRank.Of(result.Document!.SyncKind) > LyricsSyncKindRank.Of(winner.Document!.SyncKind))
            {
                winner = result;
            }
        }

        if (winner is not null)
        {
            return new LyricsFallbackDecision(
                winner,
                winner.Definitive ? LyricsCacheAction.WritePositive : LyricsCacheAction.None);
        }

        var allDefinitive = results.Count > 0 && results.All(r => r.IsDefinitive);
        return new LyricsFallbackDecision(
            Winner: null,
            allDefinitive ? LyricsCacheAction.WriteNegative : LyricsCacheAction.None);
    }
}
