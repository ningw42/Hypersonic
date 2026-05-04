using System.Text.Json;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services;

public sealed class JsonImageCacheStore : IImageCacheStore, IDisposable
{
    private const string ManifestFileName = "manifest.json";
    private static readonly TimeSpan FlushDelay = TimeSpan.FromMilliseconds(250);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _flushLock = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ILogger _log;
    private readonly string _cacheFolderPath;
    private readonly string _manifestPath;

    private ImageCacheManifest? _manifest;
    private volatile bool _isDirty;
    private Task? _backgroundFlushTask;
    private volatile bool _disposed;

    public JsonImageCacheStore(IOptions<ImageCacheOptions> options, ILoggingService logService)
    {
        _cacheFolderPath = ImageCachePath.Resolve(options.Value);
        _manifestPath = Path.Combine(_cacheFolderPath, ManifestFileName);
        _log = logService.GetLogger(nameof(JsonImageCacheStore));
    }

    public async Task<ImageCacheEntry?> GetEntryAsync(string keyHash, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestLockedAsync(cancellationToken).ConfigureAwait(false);
            return manifest.Entries.TryGetValue(keyHash, out var entry)
                ? entry.Clone()
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TouchEntryAsync(string keyHash, DateTimeOffset lastAccessUtc, CancellationToken cancellationToken = default)
    {
        var touched = false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestLockedAsync(cancellationToken).ConfigureAwait(false);
            if (manifest.Entries.TryGetValue(keyHash, out var entry))
            {
                entry.LastAccessUtc = lastAccessUtc;
                _isDirty = true;
                touched = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (touched)
        {
            QueueBackgroundFlush();
        }
    }

    public async Task UpsertEntryAsync(string keyHash, ImageCacheEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestLockedAsync(cancellationToken).ConfigureAwait(false);
            manifest.Entries[keyHash] = entry.Clone();
            _isDirty = true;
        }
        finally
        {
            _gate.Release();
        }

        QueueBackgroundFlush();
    }

    public async Task RemoveEntryAsync(
        string keyHash,
        bool flushImmediately = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestLockedAsync(cancellationToken).ConfigureAwait(false);
            if (manifest.Entries.Remove(keyHash))
            {
                _isDirty = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (flushImmediately)
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (_isDirty)
        {
            QueueBackgroundFlush();
        }
    }

    public async Task<ImageCacheStoreStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestLockedAsync(cancellationToken).ConfigureAwait(false);
            return new ImageCacheStoreStats(
                manifest.Entries.Values.Sum(static e => e.Length),
                manifest.Entries.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ImageCacheStoreEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestLockedAsync(cancellationToken).ConfigureAwait(false);
            return manifest.Entries
                .Select(static pair => new ImageCacheStoreEntry(pair.Key, pair.Value.Clone()))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ImageCacheStoreEntry>> GetEvictionCandidatesAsync(
        string? protectedKeyHash = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestLockedAsync(cancellationToken).ConfigureAwait(false);
            return manifest.Entries
                .Where(pair => !string.Equals(pair.Key, protectedKeyHash, StringComparison.Ordinal))
                .OrderBy(static pair => pair.Value.LastAccessUtc)
                .Select(static pair => new ImageCacheStoreEntry(pair.Key, pair.Value.Clone()))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_cacheFolderPath);
            _manifest = new ImageCacheManifest();
            _isDirty = true;
            await SaveManifestLockedAsync(_manifest, cancellationToken).ConfigureAwait(false);
            _isDirty = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_isDirty)
            {
                return;
            }

            var manifest = await LoadManifestLockedAsync(cancellationToken).ConfigureAwait(false);
            await SaveManifestLockedAsync(manifest, cancellationToken).ConfigureAwait(false);
            _isDirty = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogWarning("Image cache manifest final flush failed", ex);
        }

        _disposeCts.Cancel();

        Task? backgroundFlushTask;
        lock (_flushLock)
        {
            backgroundFlushTask = _backgroundFlushTask;
        }

        try
        {
            backgroundFlushTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogWarning("Image cache manifest background flush shutdown failed", ex);
        }

        _disposeCts.Dispose();
        _gate.Dispose();
    }

    private async Task<ImageCacheManifest> LoadManifestLockedAsync(CancellationToken cancellationToken)
    {
        if (_manifest != null)
        {
            return _manifest;
        }

        Directory.CreateDirectory(_cacheFolderPath);

        if (!File.Exists(_manifestPath))
        {
            _manifest = new ImageCacheManifest();
            return _manifest;
        }

        try
        {
            await using var stream = File.OpenRead(_manifestPath);
            _manifest = await JsonSerializer.DeserializeAsync<ImageCacheManifest>(
                stream,
                Json.Options,
                cancellationToken).ConfigureAwait(false) ?? new ImageCacheManifest();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LogWarning("Image cache manifest could not be read; starting with an empty manifest", ex);
            _manifest = new ImageCacheManifest();
        }

        return _manifest;
    }

    private async Task SaveManifestLockedAsync(ImageCacheManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheFolderPath);

        var tempPath = Path.Combine(_cacheFolderPath, $"{ManifestFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, Json.Options, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _manifestPath, overwrite: true);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private void QueueBackgroundFlush()
    {
        if (_disposed)
        {
            return;
        }

        lock (_flushLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_backgroundFlushTask is { IsCompleted: false })
            {
                return;
            }

            _backgroundFlushTask = Task.Run(FlushLoopAsync);
        }
    }

    private async Task FlushLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(FlushDelay, _disposeCts.Token).ConfigureAwait(false);
                await FlushAsync().ConfigureAwait(false);

                lock (_flushLock)
                {
                    if (!_isDirty)
                    {
                        _backgroundFlushTask = null;
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            lock (_flushLock)
            {
                _backgroundFlushTask = null;
            }
        }
        catch (Exception ex)
        {
            LogWarning("Image cache manifest background flush failed", ex);
            lock (_flushLock)
            {
                _backgroundFlushTask = null;
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
            System.Diagnostics.Debug.WriteLine($"[JsonImageCacheStore] {message}: {exception}");
        }
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class ImageCacheManifest
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, ImageCacheEntry> Entries { get; set; } = [];
    }
}
