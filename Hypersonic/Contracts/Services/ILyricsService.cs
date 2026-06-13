using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Contracts.Services;

/// <summary>
/// Orchestrates lyrics resolution and the durable cache for the active track. The service performs
/// its own provider retries, cross-provider fallback, and stale-fallback internally, so the returned
/// document is terminal for this attempt:
/// <list type="bullet">
///   <item><c>null</c> — no usable lyrics this attempt, not cached as definitive (a later track
///     change or app launch may try again).</item>
///   <item>A document with <see cref="LyricsSyncKind.None"/> — definitively no lyrics (negative-cached).</item>
///   <item>Any other document — loaded lyrics.</item>
/// </list>
/// The two negative outcomes differ only in caching; both present as "no lyrics" to the consumer.
/// </summary>
public interface ILyricsService
{
    /// <summary>
    /// Resolves lyrics for <paramref name="query"/>. <paramref name="budget"/> is the fan-out wait
    /// window (null → the default from options). <paramref name="forceRefresh"/> bypasses the cache
    /// read and overwrites the entry (the on-demand recovery path).
    /// </summary>
    Task<LyricsDocument?> GetLyricsAsync(
        LyricsQuery query,
        TimeSpan? budget = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task ReconcileAsync(CancellationToken cancellationToken = default);
}
