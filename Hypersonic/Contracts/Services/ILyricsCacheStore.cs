using Hypersonic.Models;
using Hypersonic.Services.Cache;

namespace Hypersonic.Contracts.Services;

/// <summary>
/// Per-entity cache store interface for durable lyrics. Mirrors <see cref="IImageCacheStore"/>
/// (entry CRUD, stats, clear, flush, atomic payload write, reconcile, eviction) but, instead of
/// handing a file <c>Uri</c> to XAML, exposes:
/// <list type="bullet">
///   <item><see cref="WriteNegativeAsync"/> — lyrics negative-cache "definitively no lyrics".</item>
///   <item><see cref="ReadPayloadAsync"/> — index row + payload bytes together, gate-consistent, for
///     in-process parse-on-read.</item>
/// </list>
/// </summary>
public interface ILyricsCacheStore
{
    string CacheFolderPath { get; }

    Task<LyricsCacheEntry?> GetEntryAsync(string keyHash, CancellationToken cancellationToken = default);

    Task TouchEntryAsync(string keyHash, DateTimeOffset lastAccessUtc, CancellationToken cancellationToken = default);

    Task UpsertEntryAsync(string keyHash, LyricsCacheEntry entry, CancellationToken cancellationToken = default);

    Task RemoveEntryAsync(string keyHash, bool flushImmediately = false, CancellationToken cancellationToken = default);

    Task<CacheStoreStats> GetStatsAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default);

    Task<Uri?> WritePayloadAsync(
        string keyHash,
        ReadOnlyMemory<byte> bytes,
        LyricsCacheEntry entry,
        CancellationToken cancellationToken = default);

    Task WriteNegativeAsync(string keyHash, LyricsCacheEntry entry, CancellationToken cancellationToken = default);

    Task<CachePayload<LyricsCacheEntry>?> ReadPayloadAsync(string keyHash, CancellationToken cancellationToken = default);

    Task ReconcileAsync(CancellationToken cancellationToken = default);

    Task EvictIfNeededAsync(string? protectedKeyHash = null, CancellationToken cancellationToken = default);
}
