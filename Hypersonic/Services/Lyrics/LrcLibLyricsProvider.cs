using System.Net;
using System.Text;
using System.Text.Json;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// The lrclib lyrics source — one self-contained provider that owns its internal
/// <c>/api/get</c> → <c>/api/search</c> fallback and candidate disambiguation, returning its single
/// best <see cref="LyricsProviderResult"/>. Stateless; uses a named <c>"lrclib"</c> client from
/// <see cref="IHttpClientFactory"/> (the factory pools/rotates handlers — correct for a singleton).
/// All content runs through the shared <see cref="ILyricsParserRegistry"/> so a fetched document
/// travels the same path as a cache-read one.
/// </summary>
internal sealed class LrcLibLyricsProvider : ILyricsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Conservative (loose) gate for lrclib's fuzzy search path — bounds regression while suppressing
    // clearly-wrong search matches. /api/get (exact server match) stays ungated. Tunable.
    private const int SearchMatchThreshold = 30;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILyricsParserRegistry _parserRegistry;
    private readonly ILogger _log;

    public LrcLibLyricsProvider(
        IHttpClientFactory httpClientFactory,
        ILyricsParserRegistry parserRegistry,
        ILoggingService logService)
    {
        _httpClientFactory = httpClientFactory;
        _parserRegistry = parserRegistry;
        _log = logService.GetLogger(nameof(LrcLibLyricsProvider));
    }

    public string Id => LrcLibLyricsConstants.ProviderId;

    public string DisplayName => "LrcLib";

    public async Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return LyricsProviderResult.Transient; // nothing queryable; do not negative-cache
        }

        var client = _httpClientFactory.CreateClient("lrclib");

        if (!string.IsNullOrWhiteSpace(query.Artist))
        {
            var getResult = await TryGetAsync(client, query, cancellationToken).ConfigureAwait(false);
            if (getResult is not null)
            {
                return getResult; // Content / DefinitiveEmpty / Transient — only a 404 returns null
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await SearchAsync(client, query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Primary lookup. Returns null ONLY on 404 (caller falls through to search).</summary>
    private async Task<LyricsProviderResult?> TryGetAsync(HttpClient client, LyricsQuery query, CancellationToken ct)
    {
        var url = $"/api/get?track_name={Uri.EscapeDataString(query.Title!)}&artist_name={Uri.EscapeDataString(query.Artist!)}";
        if (!string.IsNullOrWhiteSpace(query.Album))
        {
            url += $"&album_name={Uri.EscapeDataString(query.Album)}";
        }
        if (query.DurationSeconds is { } d)
        {
            url += $"&duration={d}";
        }

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                return LyricsProviderResult.Transient;
            }

            var json = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var record = JsonSerializer.Deserialize<LrcLibRecord>(json, JsonOptions);
            return record is null ? LyricsProviderResult.Transient : MapRecord(record);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — do not mask as transient
        }
        catch (Exception ex) // network, timeout (TaskCanceledException w/o our token), JsonException
        {
            _log.Warning("lrclib /api/get failed (transient)", new { query.Title, query.Artist }, ex);
            return LyricsProviderResult.Transient;
        }
    }

    private async Task<LyricsProviderResult> SearchAsync(HttpClient client, LyricsQuery query, CancellationToken ct)
    {
        var url = $"/api/search?track_name={Uri.EscapeDataString(query.Title!)}";
        if (!string.IsNullOrWhiteSpace(query.Artist))
        {
            url += $"&artist_name={Uri.EscapeDataString(query.Artist)}";
        }

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return LyricsProviderResult.Transient;
            }

            var json = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var records = JsonSerializer.Deserialize<LrcLibRecord[]>(json, JsonOptions);
            if (records is null || records.Length == 0)
            {
                return LyricsProviderResult.DefinitiveEmpty;
            }

            var best = records
                .Select(r => (Record: r, Score: LyricsMatchScorer.Score(query, ToCandidate(r))))
                .OrderByDescending(x => x.Score)
                .First();

            if (best.Score < SearchMatchThreshold)
            {
                return LyricsProviderResult.DefinitiveEmpty; // no confident search match
            }

            return MapRecord(best.Record);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning("lrclib /api/search failed (transient)", new { query.Title, query.Artist }, ex);
            return LyricsProviderResult.Transient;
        }
    }

    /// <summary>
    /// Prefer synced, else plain. A present-but-unparseable field is a parse failure → <see cref="LyricsProviderResult.Transient"/>
    /// (never assert "no lyrics" for a record that carries lyric text we just couldn't parse). Only a record with no
    /// lyric field present (empty/instrumental) is <see cref="LyricsProviderResult.DefinitiveEmpty"/>. All content is definitive.
    /// </summary>
    private LyricsProviderResult MapRecord(LrcLibRecord record)
    {
        var anyFieldPresent = false;

        if (!string.IsNullOrWhiteSpace(record.SyncedLyrics))
        {
            anyFieldPresent = true;
            var payload = Encoding.UTF8.GetBytes(record.SyncedLyrics);
            var doc = _parserRegistry.Parse(LyricsFormat.Lrc, payload);
            if (doc is not null)
            {
                return LyricsProviderResult.Content(doc.WithSourceProvider(Id), payload, LyricsFormat.Lrc, definitive: true);
            }
        }

        if (!string.IsNullOrWhiteSpace(record.PlainLyrics))
        {
            anyFieldPresent = true;
            var payload = Encoding.UTF8.GetBytes(record.PlainLyrics);
            var doc = _parserRegistry.Parse(LyricsFormat.PlainText, payload);
            if (doc is not null)
            {
                return LyricsProviderResult.Content(doc.WithSourceProvider(Id), payload, LyricsFormat.PlainText, definitive: true);
            }
        }

        // A field was present but nothing parsed → parse failure → Transient (don't poison the negative cache).
        // No field present → genuinely empty/instrumental → DefinitiveEmpty.
        return anyFieldPresent ? LyricsProviderResult.Transient : LyricsProviderResult.DefinitiveEmpty;
    }

    /// <summary>
    /// Projects an lrclib search record into a <see cref="LyricsCandidate"/> for the shared
    /// <see cref="LyricsMatchScorer"/>. Duration is rounded to whole seconds (lrclib reports it as a double).
    /// </summary>
    private static LyricsCandidate ToCandidate(LrcLibRecord r)
        => new(r.TrackName, r.ArtistName, r.AlbumName, r.Duration is { } d ? (int)Math.Round(d) : null);

    private sealed class LrcLibRecord
    {
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public string? AlbumName { get; set; }
        public double? Duration { get; set; }
        // Informational only: instrumental tracks return empty lyric strings, which MapRecord already
        // routes to DefinitiveEmpty — so no explicit instrumental branch is needed.
        public bool Instrumental { get; set; }
        public string? PlainLyrics { get; set; }
        public string? SyncedLyrics { get; set; }
    }
}
