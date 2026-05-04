using Hypersonic.Core.Models;

namespace Hypersonic.Contracts.Services;

public sealed record ImageCacheStats(long TotalBytes, int EntryCount, string CacheFolderPath);

public interface IImageService
{
    Task<Uri?> GetImageAsync(
        ImageReference? image,
        ImageRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<ImageCacheStats> GetStatsAsync(CancellationToken cancellationToken = default);

    Task ReconcileAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
