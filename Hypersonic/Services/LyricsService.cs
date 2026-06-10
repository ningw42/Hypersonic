using System.Collections.Concurrent;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services.Cache;
using Hypersonic.Services.Lyrics;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services;

/// <summary>
/// Orchestrator and cache-policy layer for lyrics — the analog of <see cref="CachedImageService"/>.
/// The <see cref="ILyricsCacheStore"/> owns all the mechanical work (atomic write, eviction, reconcile,
/// gate, parse-on-read bytes); this service owns the genuinely per-entity policy: the scoped cache key,
/// in-flight fetch deduplication, the cross-provider fallback, the positive/negative/TTL caching
/// decision, and stale-fallback on a transient failure.
/// </summary>
public sealed class LyricsService : ILyricsService, IDisposable
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ILyricsProvider[] _providers;
    private readonly ILyricsCacheStore _store;
    private readonly ILyricsParserRegistry _parserRegistry;
    private readonly LyricsCacheOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ConcurrentDictionary<string, Task<LyricsDocument?>> _inFlightFetches = [];
    private int _disposed;

    public LyricsService(
        ISubsonicClientService subsonicClientService,
        IEnumerable<ILyricsProvider> providers,
        ILyricsCacheStore store,
        ILyricsParserRegistry parserRegistry,
        IOptions<LyricsCacheOptions> options,
        TimeProvider timeProvider,
        ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _providers = providers.ToArray();
        _store = store;
        _parserRegistry = parserRegistry;
        _options = options.Value;
        _timeProvider = timeProvider;
        _log = logService.GetLogger(nameof(LyricsService));
    }

    public async Task<LyricsDocument?> GetLyricsAsync(LyricsQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null || string.IsNullOrWhiteSpace(query.SongId) || IsDisposed)
        {
            return null;
        }

        using var linkedCts = CreateLinkedDisposeTokenSource(cancellationToken);
        if (linkedCts is null)
        {
            return null;
        }

        var token = linkedCts.Token;

        try
        {
            if (!SubsonicCacheScope.TryBuildSongKeyHash(_subsonicClientService, query.SongId, out var keyHash))
            {
                // No active client / user identity — lookup and fetch are skipped (mirrors the image cache).
                return null;
            }

            var (hit, cachedDocument) = await TryReadCacheAsync(keyHash, allowStale: false, token).ConfigureAwait(false);
            if (hit)
            {
                return cachedDocument;
            }

            if (IsDisposed)
            {
                return null;
            }

            var fetchTask = _inFlightFetches.GetOrAdd(keyHash, _ => CreateInFlightFetchTask(query, keyHash));
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

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (IsDisposed)
        {
            return;
        }

        using var linkedCts = CreateLinkedDisposeTokenSource(cancellationToken);
        if (linkedCts is null)
        {
            return;
        }

        try
        {
            await _store.ReconcileAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
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
            LogWarning("Lyrics service cancellation failed during dispose", ex);
        }

        foreach (var disposable in _providers.OfType<IDisposable>())
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                LogWarning("Lyrics provider dispose failed", ex);
            }
        }

        _disposeCts.Dispose();
    }

    private Task<LyricsDocument?> CreateInFlightFetchTask(LyricsQuery query, string keyHash)
    {
        var task = FetchAndStoreAsync(query, keyHash, GetDisposeToken());
        _ = task.ContinueWith(
            completed => _inFlightFetches.TryRemove(new KeyValuePair<string, Task<LyricsDocument?>>(keyHash, completed)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    private async Task<LyricsDocument?> FetchAndStoreAsync(LyricsQuery query, string keyHash, CancellationToken cancellationToken)
    {
        // Yield so this task never completes synchronously inside the GetOrAdd factory — otherwise the
        // in-flight removal continuation could run before the entry is added, stranding a completed task
        // in the map and suppressing later refetches. (Real fetches yield on I/O; don't rely on that.)
        await Task.Yield();

        if (cancellationToken.IsCancellationRequested || IsDisposed)
        {
            return null;
        }

        // Sequential walk in priority order, short-circuited on the first usable content. Phase 1.5
        // swaps this for concurrent fan-out without changing the provider contract or the policy below.
        var results = new List<LyricsProviderResult>(_providers.Length);
        foreach (var provider in _providers)
        {
            if (cancellationToken.IsCancellationRequested || IsDisposed)
            {
                return null;
            }

            LyricsProviderResult result;
            try
            {
                result = await provider.FetchAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || IsDisposed)
            {
                return null;
            }
            catch (Exception ex)
            {
                _log.Warning("Lyrics provider threw; treating as transient", new { provider.Id, query.SongId }, ex);
                result = LyricsProviderResult.Transient;
            }

            results.Add(result);
            if (result.Kind == LyricsOutcomeKind.Content)
            {
                break;
            }
        }

        if (cancellationToken.IsCancellationRequested || IsDisposed)
        {
            return null;
        }

        var decision = LyricsFallbackPolicy.Decide(results);
        return decision.Action switch
        {
            LyricsCacheAction.WritePositive => await WritePositiveAsync(keyHash, decision.Winner!, cancellationToken).ConfigureAwait(false),
            LyricsCacheAction.WriteNegative => await WriteNegativeAsync(keyHash, cancellationToken).ConfigureAwait(false),
            _ when decision.Winner is not null => decision.Winner.Document, // non-definitive content: shown, not cached
            _ => (await TryReadCacheAsync(keyHash, allowStale: true, cancellationToken).ConfigureAwait(false)).Document,
        };
    }

    private async Task<LyricsDocument?> WritePositiveAsync(string keyHash, LyricsProviderResult winner, CancellationToken cancellationToken)
    {
        try
        {
            var now = _timeProvider.GetUtcNow();
            var oldEntry = await _store.GetEntryAsync(keyHash, cancellationToken).ConfigureAwait(false);
            var document = winner.Document!;

            var entry = new LyricsCacheEntry
            {
                FileName = keyHash + ExtensionForFormat(winner.PayloadFormat!),
                Length = winner.RawPayload.Length,
                SourceProvider = document.SourceProvider ?? string.Empty,
                PayloadFormat = winner.PayloadFormat!,
                SyncKind = document.SyncKind,
                LanguageCode = document.LanguageCode,
                OffsetMs = document.OffsetMs,
                CreatedAtUtc = oldEntry?.CreatedAtUtc ?? now,
                LastAccessUtc = now,
                ExpiresAtUtc = now.Add(GetPositiveTtl()),
            };

            await _store.WritePayloadAsync(keyHash, winner.RawPayload, entry, cancellationToken).ConfigureAwait(false);
            return document;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || IsDisposed)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persisting failed, but we still have the document to show this attempt.
            _log.Warning("Lyrics positive cache write failed; returning the document uncached", new { KeyHash = keyHash }, ex);
            return winner.Document;
        }
    }

    private async Task<LyricsDocument?> WriteNegativeAsync(string keyHash, CancellationToken cancellationToken)
    {
        try
        {
            var now = _timeProvider.GetUtcNow();
            var oldEntry = await _store.GetEntryAsync(keyHash, cancellationToken).ConfigureAwait(false);

            var entry = new LyricsCacheEntry
            {
                FileName = string.Empty,
                Length = 0,
                SourceProvider = _providers.Length > 0 ? _providers[0].Id : string.Empty,
                PayloadFormat = string.Empty,
                SyncKind = LyricsSyncKind.None,
                LanguageCode = null,
                OffsetMs = null,
                CreatedAtUtc = oldEntry?.CreatedAtUtc ?? now,
                LastAccessUtc = now,
                ExpiresAtUtc = now.Add(GetNegativeTtl()),
            };

            await _store.WriteNegativeAsync(keyHash, entry, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || IsDisposed)
        {
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning("Lyrics negative cache write failed", new { KeyHash = keyHash }, ex);
        }

        return LyricsDocument.Empty();
    }

    /// <summary>
    /// Cache read with parse-on-read. <paramref name="allowStale"/> ignores expiry (used for the
    /// transient stale-fallback). Returns <c>(true, doc)</c> for a usable positive hit, <c>(true,
    /// Empty)</c> for a negative hit, and <c>(false, null)</c> for a miss / expired / corrupt payload.
    /// </summary>
    private async Task<(bool Hit, LyricsDocument? Document)> TryReadCacheAsync(string keyHash, bool allowStale, CancellationToken cancellationToken)
    {
        var entry = await _store.GetEntryAsync(keyHash, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return (false, null);
        }

        var now = _timeProvider.GetUtcNow();
        if (!allowStale && entry.ExpiresAtUtc <= now)
        {
            return (false, null);
        }

        if (string.IsNullOrEmpty(entry.FileName))
        {
            // Negative-cache row.
            return (true, LyricsDocument.Empty(entry.SourceProvider));
        }

        var payload = await _store.ReadPayloadAsync(keyHash, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            // File missing (orphan row auto-removed) — treat as a miss.
            return (false, null);
        }

        var document = _parserRegistry.Parse(payload.Entry.PayloadFormat, payload.Bytes);
        if (document is null)
        {
            // Corrupt payload — degrade to a miss so a fresh fetch can replace it.
            _log.Warning("Cached lyrics payload failed to parse; treating as miss", new { KeyHash = keyHash, payload.Entry.PayloadFormat });
            return (false, null);
        }

        // The index row is the authority for source/language/offset; overlay them when the parsed
        // document lacks them (a no-op for Subsonic, where the payload already carries them, but it
        // lets future metadata-poor formats — LRC/TTML/YRC — recover them without contract churn).
        document = OverlayRowMetadata(document, payload.Entry);

        await _store.TouchEntryAsync(keyHash, now, cancellationToken).ConfigureAwait(false);
        return (true, document);
    }

    /// <summary>
    /// Fills <see cref="LyricsDocument"/> source/language/offset from the index row when the parser
    /// left them null; the parsed document otherwise wins. Sync kind and lines are always the parser's.
    /// </summary>
    private static LyricsDocument OverlayRowMetadata(LyricsDocument document, LyricsCacheEntry entry)
    {
        var source = document.SourceProvider ?? (string.IsNullOrEmpty(entry.SourceProvider) ? null : entry.SourceProvider);
        var language = document.LanguageCode ?? entry.LanguageCode;
        var offset = document.OffsetMs ?? entry.OffsetMs;

        if (source == document.SourceProvider && language == document.LanguageCode && offset == document.OffsetMs)
        {
            return document; // nothing to overlay — avoid rebuilding the document (the common case)
        }

        return new LyricsDocument
        {
            SyncKind = document.SyncKind,
            Lines = document.Lines,
            SourceProvider = source,
            LanguageCode = language,
            OffsetMs = offset,
        };
    }

    /// <summary>Maps a payload format tag to the file extension its cached payload uses.</summary>
    private static string ExtensionForFormat(string formatTag) => formatTag switch
    {
        SubsonicLyricsConstants.StructuredFormat => ".json",
        SubsonicLyricsConstants.LegacyFormat => ".txt",
        _ => ".txt",
    };

    private TimeSpan GetPositiveTtl()
        => TimeSpan.FromDays(_options.PositiveTimeToLiveDays > 0 ? _options.PositiveTimeToLiveDays : 30);

    private TimeSpan GetNegativeTtl()
        => TimeSpan.FromDays(_options.NegativeTimeToLiveDays > 0 ? _options.NegativeTimeToLiveDays : 7);

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

    private void LogWarning(string message, Exception exception)
    {
        try
        {
            _log.Warning(message, exception: exception);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsService] {message}: {exception}");
        }
    }
}
