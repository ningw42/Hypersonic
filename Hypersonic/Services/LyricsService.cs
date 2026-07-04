using System.Collections.Concurrent;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;
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
    private readonly ILocalLyricsOverrideSource? _overrideSource;
    private readonly LyricsCacheOptions _options;
    private readonly LyricsOrchestrationOptions _orchestrationOptions;
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
        IOptions<LyricsOrchestrationOptions> orchestrationOptions,
        TimeProvider timeProvider,
        ILoggingService logService,
        ILocalLyricsOverrideSource? overrideSource = null)
    {
        _subsonicClientService = subsonicClientService;
        _providers = providers.ToArray();
        _store = store;
        _parserRegistry = parserRegistry;
        _overrideSource = overrideSource;
        _options = options.Value;
        _orchestrationOptions = orchestrationOptions.Value;
        _timeProvider = timeProvider;
        _log = logService.GetLogger(nameof(LyricsService));
    }

    public async Task<LyricsDocument?> GetLyricsAsync(
        LyricsQuery query, TimeSpan? budget = null, bool forceRefresh = false, CancellationToken cancellationToken = default)
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
            // Local override wins outright (Phase 2 §3.8): a sideloaded <songId>.ttml takes hard
            // precedence over the cache and every provider — bypassing the cache read, the fan-out, and
            // any cache write. Absent → fall through to the unchanged cache → fan-out path. The check is
            // a cheap existence test run only here (track change), never per frame.
            if (_overrideSource is not null)
            {
                var overrideDocument = await _overrideSource.TryGetOverrideAsync(query.SongId, token).ConfigureAwait(false);
                if (overrideDocument is not null)
                {
                    return overrideDocument;
                }
            }

            if (!SubsonicCacheScope.TryBuildSongKeyHash(_subsonicClientService, query.SongId, out var keyHash))
            {
                return null;
            }

            if (!forceRefresh)
            {
                var (hit, cachedDocument) = await TryReadCacheAsync(keyHash, allowStale: false, token).ConfigureAwait(false);
                if (hit)
                {
                    return cachedDocument;
                }
            }

            if (IsDisposed)
            {
                return null;
            }

            var defaultBudgetMs = _orchestrationOptions.DefaultFanOutBudgetMs > 0 ? _orchestrationOptions.DefaultFanOutBudgetMs : 700;
            var resolvedBudget = budget ?? TimeSpan.FromMilliseconds(defaultBudgetMs);
            if (resolvedBudget < TimeSpan.Zero)
            {
                // A caller-supplied negative budget (incl. the -1ms "infinite" sentinel) would throw in Task.Delay
                // or disable truncation — fall back to the default wait window.
                resolvedBudget = TimeSpan.FromMilliseconds(defaultBudgetMs);
            }

            var fetchTask = _inFlightFetches.GetOrAdd(keyHash, _ => CreateInFlightFetchTask(query, keyHash, resolvedBudget));
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

    private Task<LyricsDocument?> CreateInFlightFetchTask(LyricsQuery query, string keyHash, TimeSpan budget)
    {
        var task = FetchAndStoreAsync(query, keyHash, budget, GetDisposeToken());
        _ = task.ContinueWith(
            completed => _inFlightFetches.TryRemove(new KeyValuePair<string, Task<LyricsDocument?>>(keyHash, completed)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    private async Task<LyricsDocument?> FetchAndStoreAsync(LyricsQuery query, string keyHash, TimeSpan budget, CancellationToken cancellationToken)
    {
        // Yield so this task never completes synchronously inside the GetOrAdd factory — otherwise the
        // in-flight removal continuation could run before the entry is added, stranding a completed task
        // in the map and suppressing later refetches. (Real fetches yield on I/O; don't rely on that.)
        await Task.Yield();

        if (cancellationToken.IsCancellationRequested || IsDisposed)
        {
            return null;
        }

        var (completed, anyTruncated) = await RunFanOutAsync(query, budget, cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested || IsDisposed)
        {
            return null;
        }

        var decision = LyricsFallbackPolicy.Decide(completed);
        return decision.Action switch
        {
            // Eager-pin the winner at full TTL even if a peer was truncated.
            LyricsCacheAction.WritePositive => await WritePositiveAsync(keyHash, decision.Winner!, cancellationToken).ConfigureAwait(false),
            // Suppress the negative write when a peer was truncated/incomplete (it might have had content).
            LyricsCacheAction.WriteNegative when !anyTruncated => await WriteNegativeAsync(keyHash, cancellationToken).ConfigureAwait(false),
            LyricsCacheAction.WriteNegative => LyricsDocument.Empty(), // show "no lyrics" this attempt, do not pin
            _ when decision.Winner is not null => decision.Winner.Document, // non-definitive content: shown, not cached
            _ => (await TryReadCacheAsync(keyHash, allowStale: true, cancellationToken).ConfigureAwait(false)).Document,
        };
    }

    /// <summary>
    /// Fires all providers concurrently under one linked CTS, bounded by <paramref name="budget"/>.
    /// Decides the moment all providers complete; if the budget elapses first, cancels the stragglers
    /// and decides on what completed. The returned list is
    /// ordered by provider precedence (registration order), NOT completion order — load-bearing for the
    /// policy's equal-tier tiebreak. <c>anyTruncated</c> is "any provider slot unfilled at decision time".
    /// </summary>
    private async Task<(IReadOnlyList<LyricsProviderResult> Completed, bool AnyTruncated)> RunFanOutAsync(
        LyricsQuery query, TimeSpan budget, CancellationToken cancellationToken)
    {
        var n = _providers.Length;
        if (n == 0)
        {
            return (Array.Empty<LyricsProviderResult>(), false);
        }

        var slots = new LyricsProviderResult?[n];
        var earlyExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = n;

        using var fanoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = fanoutCts.Token;

        var providerTasks = new Task[n];
        for (var i = 0; i < n; i++)
        {
            var idx = i;
            providerTasks[idx] = RunOne(idx);
        }

        // Budget timer on the injected TimeProvider (deterministic under FakeTimeProvider). Rides
        // fanoutCts.Token so an early-exit win — or a dispose/parent cancel — tears the timer down at
        // once via fanoutCts.Cancel(), rather than leaving it ticking for the full budget.
        var budgetTask = Task.Delay(budget, _timeProvider, token);
        await Task.WhenAny(earlyExit.Task, budgetTask).ConfigureAwait(false);

        // Snapshot at decision time, THEN cancel any stragglers.
        var snapshot = new LyricsProviderResult?[n];
        for (var i = 0; i < n; i++)
        {
            snapshot[i] = Volatile.Read(ref slots[i]);
        }

        try
        {
            fanoutCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        var completed = new List<LyricsProviderResult>(n);
        var anyTruncated = false;
        for (var i = 0; i < n; i++)
        {
            if (snapshot[i] is { } result)
            {
                completed.Add(result);
            }
            else
            {
                anyTruncated = true;
            }
        }

        return (completed, anyTruncated);

        async Task RunOne(int idx)
        {
            LyricsProviderResult? result = null;
            try
            {
                result = await _providers[idx].FetchAsync(query, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Truncated (budget) or disposed — leave the slot null.
            }
            catch (Exception ex)
            {
                _log.Warning("Lyrics provider threw; treating as transient", new { _providers[idx].Id, query.SongId }, ex);
                result = LyricsProviderResult.Transient;
            }

            if (result is not null)
            {
                Volatile.Write(ref slots[idx], result);
            }

            if (Interlocked.Decrement(ref remaining) == 0)
            {
                earlyExit.TrySetResult(); // all providers complete
            }
        }
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
    internal static string ExtensionForFormat(string formatTag) => formatTag switch
    {
        SubsonicLyricsConstants.StructuredFormat => ".json",
        SubsonicLyricsConstants.LegacyFormat => ".txt",
        LrcLibLyricsConstants.SyncedFormat => ".lrc",
        LrcLibLyricsConstants.PlainFormat => ".txt",
        TtmlLyricsConstants.Format => ".ttml",
        QQLyricsConstants.QrcFormat => ".qrc",
        KugouLyricsConstants.KrcFormat => ".krc",
        NeteaseLyricsConstants.YrcFormat => ".yrc",
        NeteaseLyricsConstants.LrcFormat => ".lrc",
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
