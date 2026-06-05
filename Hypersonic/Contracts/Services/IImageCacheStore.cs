using Hypersonic.Services.Cache;

namespace Hypersonic.Contracts.Services;

public sealed class ImageCacheEntry : ICacheEntry
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

/// <summary>
/// Per-entity cache store interface for cover-art / cached images. Inherits the
/// generic surface (stats, clear, flush, entry CRUD, payload write/read, reconcile,
/// eviction) implicitly via the implementing <see cref="Services.SqliteImageCacheStore"/>
/// subclass of <see cref="SqliteCacheStoreBase{TEntry}"/>. This interface only
/// names the methods consumers (CachedImageService, settings UI) bind against, in
/// terms of <see cref="ImageCacheEntry"/>.
/// </summary>
public interface IImageCacheStore
{
    string CacheFolderPath { get; }

    Task<ImageCacheEntry?> GetEntryAsync(string keyHash, CancellationToken cancellationToken = default);

    Task TouchEntryAsync(string keyHash, DateTimeOffset lastAccessUtc, CancellationToken cancellationToken = default);

    Task UpsertEntryAsync(string keyHash, ImageCacheEntry entry, CancellationToken cancellationToken = default);

    Task RemoveEntryAsync(string keyHash, bool flushImmediately = false, CancellationToken cancellationToken = default);

    Task<CacheStoreStats> GetStatsAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default);

    Task<Uri?> WritePayloadAsync(
        string keyHash,
        ReadOnlyMemory<byte> bytes,
        ImageCacheEntry entry,
        CancellationToken cancellationToken = default);

    Task<Uri?> TryGetPayloadUriAsync(string keyHash, CancellationToken cancellationToken = default);

    Task ReconcileAsync(CancellationToken cancellationToken = default);

    Task EvictIfNeededAsync(string? protectedKeyHash = null, CancellationToken cancellationToken = default);
}
