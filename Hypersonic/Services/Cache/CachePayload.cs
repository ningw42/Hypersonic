namespace Hypersonic.Services.Cache;

/// <summary>
/// An index row plus its payload bytes, returned together by
/// <see cref="SqliteCacheStoreBase{TEntry}.ReadPayloadAsync"/> so a parse-on-read caller gets the
/// row and bytes atomically (under the gate) with respect to eviction and orphan-row cleanup.
/// </summary>
public sealed record CachePayload<TEntry>(TEntry Entry, ReadOnlyMemory<byte> Bytes)
    where TEntry : ICacheEntry;
