namespace Hypersonic.Services.Cache;

/// <summary>Aggregate stats for a single cache store: total bytes on disk and row count.</summary>
public sealed record CacheStoreStats(long TotalBytes, int EntryCount);
