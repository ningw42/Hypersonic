using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services;

public sealed class CachedImageService : IImageService, IDisposable
{
    private static readonly TimeSpan ShutdownGateWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly IImageCacheStore _store;
    private readonly ImageCacheOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ConcurrentDictionary<string, Task<Uri?>> _inFlightFetches = [];
    private readonly IImageResolver[] _resolvers;
    private readonly string _cacheFolderPath;
    private int _disposed;

    public CachedImageService(
        ISubsonicClientService subsonicClientService,
        IImageCacheStore store,
        IOptions<ImageCacheOptions> options,
        TimeProvider timeProvider,
        ILoggingService logService)
    {
        _store = store;
        _options = options.Value;
        _timeProvider = timeProvider;
        _log = logService.GetLogger(nameof(CachedImageService));
        _cacheFolderPath = ImageCachePath.Resolve(_options);
        _resolvers =
        [
            new SubsonicImageResolver(subsonicClientService, logService),
            new RemoteImageResolver(logService),
        ];
    }

    public async Task<Uri?> GetImageAsync(
        ImageReference? image,
        ImageRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (image == null || IsDisposed)
        {
            return null;
        }

        using var linkedCts = CreateLinkedDisposeTokenSource(cancellationToken);
        if (linkedCts == null)
        {
            return null;
        }

        var token = linkedCts.Token;
        options ??= ImageRequestOptions.Default;

        try
        {
            IImageResolver? resolver = null;
            ImageCacheKey key = default!;
            foreach (var candidate in _resolvers)
            {
                if (candidate.TryCreateCacheKey(image, options, out key))
                {
                    resolver = candidate;
                    break;
                }
            }

            if (resolver == null)
            {
                return null;
            }

            var keyHash = Hash(key.ToMaterial());
            var cached = await TryGetCachedUriAsync(keyHash, allowStale: false, token).ConfigureAwait(false);
            if (cached != null)
            {
                return cached;
            }

            if (IsDisposed)
            {
                return null;
            }

            var fetchTask = _inFlightFetches.GetOrAdd(
                keyHash,
                _ => CreateInFlightFetchTask(resolver, image, options, key, keyHash));

            return await fetchTask.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
            return null;
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            return null;
        }
    }

    public async Task<ImageCacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var stats = await _store.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        return new ImageCacheStats(stats.TotalBytes, stats.EntryCount, _cacheFolderPath);
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (IsDisposed)
        {
            return;
        }

        using var linkedCts = CreateLinkedDisposeTokenSource(cancellationToken);
        if (linkedCts == null)
        {
            return;
        }

        var token = linkedCts.Token;
        var gateEntered = false;
        try
        {
            await _writeGate.WaitAsync(token).ConfigureAwait(false);
            gateEntered = true;

            Directory.CreateDirectory(_cacheFolderPath);

            var changed = false;
            IReadOnlyList<ImageCacheStoreEntry> entries;
            try
            {
                entries = await _store.GetEntriesAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not ObjectDisposedException)
            {
                // If we can't enumerate the manifest reliably, treating it as empty would delete every
                // cached image file. Abort the reconcile and try again next cycle.
                _log.Warning("Image cache reconcile aborted: store entry enumeration failed", exception: ex);
                return;
            }

            var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in entries)
            {
                knownFiles.Add(item.Entry.FileName);
                if (!File.Exists(Path.Combine(_cacheFolderPath, item.Entry.FileName)))
                {
                    await _store.RemoveEntryAsync(item.KeyHash, cancellationToken: token).ConfigureAwait(false);
                    changed = true;
                }
            }

            foreach (var file in Directory.EnumerateFiles(_cacheFolderPath))
            {
                token.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(file);
                if (!SqliteImageCacheStore.IsCachedImageFileName(fileName))
                {
                    continue;
                }

                if (knownFiles.Contains(fileName))
                {
                    continue;
                }

                TryDeleteFile(file, logFailures: false);
            }

            if (changed)
            {
                await _store.FlushAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
        }
        finally
        {
            if (gateEntered)
            {
                _writeGate.Release();
            }
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (IsDisposed)
        {
            return;
        }

        using var linkedCts = CreateLinkedDisposeTokenSource(cancellationToken);
        if (linkedCts == null)
        {
            return;
        }

        var token = linkedCts.Token;
        var gateEntered = false;
        try
        {
            await _writeGate.WaitAsync(token).ConfigureAwait(false);
            gateEntered = true;

            Directory.CreateDirectory(_cacheFolderPath);

            foreach (var file in Directory.EnumerateFiles(_cacheFolderPath))
            {
                token.ThrowIfCancellationRequested();

                if (!SqliteImageCacheStore.IsCachedImageFileName(Path.GetFileName(file)))
                {
                    continue;
                }

                TryDeleteFile(file, logFailures: true);
            }

            await _store.ClearAsync(token).ConfigureAwait(false);
            _log.Info("Image cache cleared", new { CacheFolder = _cacheFolderPath });
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
        }
        finally
        {
            if (gateEntered)
            {
                _writeGate.Release();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _disposeCts.Cancel();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or AggregateException)
        {
            LogWarning("Image service cancellation failed during dispose", ex);
        }

        var writeGateDrained = WaitForWriteGateToDrain();

        foreach (var disposable in _resolvers.OfType<IDisposable>())
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                LogWarning("Image resolver dispose failed", ex);
            }
        }

        if (writeGateDrained)
        {
            _writeGate.Dispose();
        }

        _disposeCts.Dispose();
    }

    private Task<Uri?> CreateInFlightFetchTask(
        IImageResolver resolver,
        ImageReference image,
        ImageRequestOptions options,
        ImageCacheKey key,
        string keyHash)
    {
        _log.Debug("Image fetch starting", new { KeyHash = keyHash, key.Provider, key.Kind, key.Size });

        var task = FetchAndStoreAsync(resolver, image, options, key, keyHash, GetDisposeToken());
        _ = task.ContinueWith(
            completed => _inFlightFetches.TryRemove(new KeyValuePair<string, Task<Uri?>>(keyHash, completed)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    private async Task<Uri?> FetchAndStoreAsync(
        IImageResolver resolver,
        ImageReference image,
        ImageRequestOptions options,
        ImageCacheKey key,
        string keyHash,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || IsDisposed)
        {
            return null;
        }

        byte[] bytes;

        try
        {
            await using var stream = await resolver.FetchAsync(image, options, cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            bytes = memory.ToArray();

            if (bytes.Length == 0)
            {
                throw new InvalidDataException("Image response was empty.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested || IsDisposed)
            {
                return null;
            }

            _log.Warning("Image fetch failed, falling back to stale cache entry if present", new { KeyHash = keyHash, key.Provider, key.Kind }, ex);
            return await TryGetCachedUriAsync(keyHash, allowStale: true, cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            return null;
        }

        if (cancellationToken.IsCancellationRequested || IsDisposed)
        {
            return null;
        }

        var gateEntered = false;
        string? tempPath = null;
        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateEntered = true;

            Directory.CreateDirectory(_cacheFolderPath);

            var now = _timeProvider.GetUtcNow();
            var oldEntry = await _store.GetEntryAsync(keyHash, cancellationToken).ConfigureAwait(false);
            var extension = DetectExtension(bytes);
            var fileName = $"{keyHash}{extension}";
            var finalPath = Path.Combine(_cacheFolderPath, fileName);
            tempPath = Path.Combine(_cacheFolderPath, $"{keyHash}.{Guid.NewGuid():N}.tmp");

            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, finalPath, overwrite: true);
            tempPath = null;

            if (oldEntry != null &&
                !string.Equals(oldEntry.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(Path.Combine(_cacheFolderPath, oldEntry.FileName), logFailures: false);
            }

            await _store.UpsertEntryAsync(
                keyHash,
                new ImageCacheEntry
                {
                    FileName = fileName,
                    Length = bytes.LongLength,
                    Provider = key.Provider,
                    Kind = key.Kind,
                    ServerHash = key.ServerHash,
                    ServerScopeHash = key.ServerScopeHash,
                    Size = key.Size,
                    CreatedAtUtc = oldEntry?.CreatedAtUtc ?? now,
                    LastAccessUtc = now,
                    ExpiresAtUtc = now.Add(GetTtl()),
                },
                cancellationToken).ConfigureAwait(false);

            await EvictIfNeededAsync(keyHash, cancellationToken).ConfigureAwait(false);

            _log.Debug(
                "Image cached",
                new
                {
                    KeyHash = keyHash,
                    key.Provider,
                    key.Kind,
                    ServerHash = ShortHash(key.ServerHash),
                    ServerScopeHash = ShortHash(key.ServerScopeHash),
                    key.Size,
                    bytes.LongLength,
                });
            return new Uri(finalPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || IsDisposed)
        {
            if (tempPath != null)
            {
                TryDeleteFile(tempPath, logFailures: false);
            }

            return null;
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            if (tempPath != null)
            {
                TryDeleteFile(tempPath, logFailures: false);
            }

            return null;
        }
        finally
        {
            if (gateEntered)
            {
                _writeGate.Release();
            }
        }
    }

    private async Task<Uri?> TryGetCachedUriAsync(string keyHash, bool allowStale, CancellationToken cancellationToken)
    {
        var entry = await _store.GetEntryAsync(keyHash, cancellationToken).ConfigureAwait(false);
        if (entry == null)
        {
            _log.Debug("Cache miss", new { KeyHash = keyHash, Reason = "no-entry", AllowStale = allowStale });
            return null;
        }

        var path = Path.Combine(_cacheFolderPath, entry.FileName);
        if (!File.Exists(path))
        {
            await _store.RemoveEntryAsync(keyHash, flushImmediately: true, cancellationToken).ConfigureAwait(false);
            _log.Debug("Cache miss", new { KeyHash = keyHash, Reason = "file-missing", AllowStale = allowStale });
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        if (!allowStale && entry.ExpiresAtUtc <= now)
        {
            _log.Debug("Cache miss", new { KeyHash = keyHash, Reason = "expired", AllowStale = allowStale });
            return null;
        }

        await _store.TouchEntryAsync(keyHash, now, cancellationToken).ConfigureAwait(false);
        _log.Debug(
            allowStale ? "Cache hit (stale fallback)" : "Cache hit",
            new { KeyHash = keyHash, entry.Provider, entry.Kind, entry.Size, entry.Length });
        return new Uri(path);
    }

    private async Task EvictIfNeededAsync(string protectedKeyHash, CancellationToken cancellationToken)
    {
        var maximumBytes = _options.MaximumBytes > 0
            ? _options.MaximumBytes
            : 256L * 1024L * 1024L;

        var stats = await _store.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        var totalBytes = stats.TotalBytes;
        if (totalBytes <= maximumBytes)
        {
            return;
        }

        var startingBytes = totalBytes;
        var evictedCount = 0;
        var changed = false;
        foreach (var item in await _store.GetEvictionCandidatesAsync(protectedKeyHash, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (totalBytes <= maximumBytes)
            {
                break;
            }

            TryDeleteFile(Path.Combine(_cacheFolderPath, item.Entry.FileName), logFailures: false);
            await _store.RemoveEntryAsync(item.KeyHash, cancellationToken: cancellationToken).ConfigureAwait(false);
            totalBytes -= item.Entry.Length;
            evictedCount++;
            changed = true;
        }

        if (changed)
        {
            await _store.FlushAsync(cancellationToken).ConfigureAwait(false);
            _log.Debug(
                "Image cache eviction",
                new
                {
                    EvictedCount = evictedCount,
                    BytesFreed = startingBytes - totalBytes,
                    TotalBytes = totalBytes,
                    MaximumBytes = maximumBytes,
                });
        }
    }

    private void TryDeleteFile(string path, bool logFailures)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (logFailures)
            {
                _log.Warning("Cached image file could not be deleted", new { FileName = Path.GetFileName(path) }, ex);
            }
            else
            {
                _log.Debug("Cached image file delete skipped", new { FileName = Path.GetFileName(path) });
            }
        }
    }

    private void LogWarning(string message, Exception exception)
    {
        try
        {
            _log.Warning(message, exception: exception);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"[CachedImageService] {message}: {exception}");
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private CancellationTokenSource? CreateLinkedDisposeTokenSource(CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return null;
        }

        try
        {
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            return null;
        }
    }

    private CancellationToken GetDisposeToken()
    {
        try
        {
            return _disposeCts.Token;
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            return new CancellationToken(canceled: true);
        }
    }

    private bool WaitForWriteGateToDrain()
    {
        var gateEntered = false;
        try
        {
            if (!_writeGate.Wait(ShutdownGateWaitTimeout))
            {
                LogWarning(
                    "Image service write gate did not drain before shutdown timeout",
                    new TimeoutException($"Timed out after {ShutdownGateWaitTimeout}."));
                return false;
            }

            gateEntered = true;
            return true;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
        catch (Exception ex)
        {
            LogWarning("Image service write gate shutdown wait failed", ex);
            return false;
        }
        finally
        {
            if (gateEntered)
            {
                _writeGate.Release();
            }
        }
    }

    private TimeSpan GetTtl()
    {
        var days = _options.TimeToLiveDays > 0 ? _options.TimeToLiveDays : 30;
        return TimeSpan.FromDays(days);
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ShortHash(string hash) => hash.Length >= 8 ? hash[..8] : hash;

    private static string DetectExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return ".jpg";
        }

        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 &&
            bytes[1] == 0x49 &&
            bytes[2] == 0x46)
        {
            return ".gif";
        }

        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 &&
            bytes[1] == 0x49 &&
            bytes[2] == 0x46 &&
            bytes[3] == 0x46 &&
            bytes[8] == 0x57 &&
            bytes[9] == 0x45 &&
            bytes[10] == 0x42 &&
            bytes[11] == 0x50)
        {
            return ".webp";
        }

        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return ".bmp";
        }

        return ".img";
    }
}
