namespace Hypersonic.Contracts.Services;

public sealed record ImageCacheStoreStats(long TotalBytes, int EntryCount);

public sealed record ImageCacheStoreEntry(string KeyHash, ImageCacheEntry Entry);

public sealed class ImageCacheEntry
{
    public string FileName { get; set; } = string.Empty;

    public long Length { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string ServerHash { get; set; } = string.Empty;

    public string ServerScopeHash { get; set; } = string.Empty;

    public string Size { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset LastAccessUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public ImageCacheEntry Clone()
    {
        return new ImageCacheEntry
        {
            FileName = FileName,
            Length = Length,
            Provider = Provider,
            Kind = Kind,
            ServerHash = ServerHash,
            ServerScopeHash = ServerScopeHash,
            Size = Size,
            CreatedAtUtc = CreatedAtUtc,
            LastAccessUtc = LastAccessUtc,
            ExpiresAtUtc = ExpiresAtUtc,
        };
    }
}

public interface IImageCacheStore
{
    Task<ImageCacheEntry?> GetEntryAsync(string keyHash, CancellationToken cancellationToken = default);

    Task TouchEntryAsync(string keyHash, DateTimeOffset lastAccessUtc, CancellationToken cancellationToken = default);

    Task UpsertEntryAsync(string keyHash, ImageCacheEntry entry, CancellationToken cancellationToken = default);

    Task RemoveEntryAsync(string keyHash, bool flushImmediately = false, CancellationToken cancellationToken = default);

    Task<ImageCacheStoreStats> GetStatsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageCacheStoreEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageCacheStoreEntry>> GetEvictionCandidatesAsync(
        string? protectedKeyHash = null,
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
