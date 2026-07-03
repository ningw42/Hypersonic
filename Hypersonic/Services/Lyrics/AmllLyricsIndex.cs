using System.Text.Json;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Stateful shell around <see cref="AmllLyricsIndexData"/>: owns the disk cache, the ETag-conditional
/// CDN refresh, and readiness. Single writer, singleton. See the design doc for lifecycle.
/// </summary>
public sealed class AmllLyricsIndex : IAmllLyricsIndex
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppFolderLocator _folderLocator;
    private readonly AmllLyricsOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _log;
    private volatile AmllLyricsIndexData? _data;

    public AmllLyricsIndex(
        IHttpClientFactory httpClientFactory,
        IAppFolderLocator folderLocator,
        IOptions<AmllLyricsOptions> options,
        TimeProvider timeProvider,
        ILoggingService logService)
    {
        _httpClientFactory = httpClientFactory;
        _folderLocator = folderLocator;
        _options = options.Value;
        _timeProvider = timeProvider;
        _log = logService.GetLogger(nameof(AmllLyricsIndex));
    }

    public bool IsReady => _data is not null;

    public string? TryResolve(LyricsQuery query) => _data?.TryResolve(query);

    public async Task WarmAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var (bytes, meta) = TryReadDisk();
        if (bytes is not null)
        {
            _data = AmllLyricsIndexData.Parse(bytes);
        }

        var stale = meta is null
            || _timeProvider.GetUtcNow() - meta.FetchedUtc > TimeSpan.FromDays(_options.RefreshIntervalDays);
        if (bytes is not null && !stale)
        {
            return; // fresh disk copy — no network
        }

        await RefreshFromNetworkAsync(meta?.ETag, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshFromNetworkAsync(string? etag, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(AmllLyricsConstants.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, AmllLyricsConstants.IndexPath);
            if (!string.IsNullOrEmpty(etag))
            {
                request.Headers.IfNoneMatch.Add(System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag));
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow();

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                WriteMeta(new AmllIndexMeta(etag, now)); // keep disk data, restamp freshness
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("AMLL index refresh non-success; keeping existing index",
                    new { Status = (int)response.StatusCode });
                return;
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var data = AmllLyricsIndexData.Parse(body);
            WriteIndexAtomic(body);
            WriteMeta(new AmllIndexMeta(response.Headers.ETag?.ToString(), now));
            _data = data; // atomic swap after a successful parse + persist
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning("AMLL index refresh failed; keeping existing index", exception: ex);
        }
    }

    private void WriteIndexAtomic(byte[] body)
    {
        var sub = Path.Combine(_folderLocator.GetCacheFolder(), AmllLyricsConstants.CacheSubfolder);
        Directory.CreateDirectory(sub);
        var target = Path.Combine(sub, AmllLyricsConstants.IndexFileName);
        var temp = target + ".tmp";
        File.WriteAllBytes(temp, body);
        File.Move(temp, target, overwrite: true);
    }

    private void WriteMeta(AmllIndexMeta meta)
    {
        // Bare JsonSerializer (not the project Json helper) is deliberate and self-consistent here: the
        // sidecar is PascalCase, round-trips write↔read cleanly, and has no enums needing a custom converter.
        var sub = Path.Combine(_folderLocator.GetCacheFolder(), AmllLyricsConstants.CacheSubfolder);
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, AmllLyricsConstants.MetaFileName), JsonSerializer.SerializeToUtf8Bytes(meta));
    }

    private (byte[]? Bytes, AmllIndexMeta? Meta) TryReadDisk()
    {
        try
        {
            var sub = Path.Combine(_folderLocator.GetCacheFolder(), AmllLyricsConstants.CacheSubfolder);
            var indexPath = Path.Combine(sub, AmllLyricsConstants.IndexFileName);
            var metaPath = Path.Combine(sub, AmllLyricsConstants.MetaFileName);
            if (!File.Exists(indexPath))
            {
                return (null, null);
            }

            var bytes = File.ReadAllBytes(indexPath);
            AmllIndexMeta? meta = null;
            if (File.Exists(metaPath))
            {
                meta = JsonSerializer.Deserialize<AmllIndexMeta>(File.ReadAllBytes(metaPath));
            }

            return (bytes, meta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Warning("Reading cached AMLL index failed; treating as absent", exception: ex);
            return (null, null);
        }
    }

    internal sealed record AmllIndexMeta(string? ETag, DateTimeOffset FetchedUtc);
}
