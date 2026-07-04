using System.Text;
using System.Text.Json;

using Hypersonic.Client.Models.Entities;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// The Subsonic lyrics source. One self-contained provider that owns the internal
/// structured (<c>getLyricsBySongId</c>) → legacy (<c>getLyrics</c>) fallback (the relocated
/// <c>LyricsFetchPolicy.Combine</c> semantics) and returns its single best
/// <see cref="LyricsProviderResult"/>. Both endpoints run through the shared
/// <see cref="ILyricsParserRegistry"/> so a fetched document travels the same path as a
/// cache-read one.
/// </summary>
internal sealed class SubsonicLyricsProvider : ILyricsProvider
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ILyricsParserRegistry _parserRegistry;
    private readonly ILogger _log;

    public SubsonicLyricsProvider(
        ISubsonicClientService subsonicClientService,
        ILyricsParserRegistry parserRegistry,
        ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _parserRegistry = parserRegistry;
        _log = logService.GetLogger(nameof(SubsonicLyricsProvider));
    }

    public string Id => SubsonicLyricsConstants.ProviderId;

    public string DisplayName => "Subsonic";

    public async Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        // Honor cancellation before any work — the provider contract must not hit an endpoint or
        // report success once the caller has cancelled, even if the service usually guards this first.
        cancellationToken.ThrowIfCancellationRequested();

        var client = _subsonicClientService.Client;
        if (client is null)
        {
            return LyricsProviderResult.Transient;
        }

        // --- Structured endpoint ---
        var (structuredContent, structuredKind) = await FetchStructuredAsync(client, query, cancellationToken).ConfigureAwait(false);
        if (structuredContent is not null)
        {
            // Structured content short-circuits: legacy is never called, and the answer is cacheable.
            return structuredContent;
        }

        // Observe caller cancellation before spending the second endpoint call.
        cancellationToken.ThrowIfCancellationRequested();

        // --- Legacy endpoint (structured produced no content) ---
        var legacyContent = await FetchLegacyAsync(client, query, structuredDefinitive: structuredKind != LyricsOutcomeKind.Transient, cancellationToken)
            .ConfigureAwait(false);
        if (legacyContent.Content is not null)
        {
            return legacyContent.Content;
        }

        // Both endpoints ended without content. Negative-cacheable only when BOTH were definitive;
        // any transient outcome blocks the negative write.
        var bothDefinitive = structuredKind != LyricsOutcomeKind.Transient
            && legacyContent.Kind != LyricsOutcomeKind.Transient;
        return bothDefinitive ? LyricsProviderResult.DefinitiveEmpty : LyricsProviderResult.Transient;
    }

    private async Task<(LyricsProviderResult? Content, LyricsOutcomeKind Kind)> FetchStructuredAsync(
        Hypersonic.Client.ISubsonicClient client,
        LyricsQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetLyricsBySongIdAsync(query.SongId, cancellationToken).ConfigureAwait(false);
            if (response.Status == "failed")
            {
                var kind = SubsonicLyricsClassifier.ClassifyFailedStatus(response.Error?.Code ?? 0);
                if (kind == LyricsOutcomeKind.Transient)
                {
                    _log.Warning("Structured lyrics fetch failed (transient)", new { query.SongId, Code = response.Error?.Code ?? 0 });
                }

                return (null, kind);
            }

            var best = SelectBestStructuredContent(response.LyricsList?.StructuredLyrics);
            if (best is null)
            {
                // Server answered cleanly but no entry yielded a usable document — definitively empty.
                return (null, LyricsOutcomeKind.DefinitiveEmpty);
            }

            var content = LyricsProviderResult.Content(
                best.Value.Document.WithSourceProvider(Id), best.Value.Payload, LyricsFormat.SubsonicStructured, definitive: true);
            return (content, LyricsOutcomeKind.Content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — do not mask it as a provider transient
        }
        catch (Exception ex)
        {
            // Includes HTTP timeouts (TaskCanceledException without our token) → transient.
            _log.Warning("Structured lyrics fetch threw", new { query.SongId }, ex);
            return (null, LyricsOutcomeKind.Transient);
        }
    }

    private async Task<(LyricsProviderResult? Content, LyricsOutcomeKind Kind)> FetchLegacyAsync(
        Hypersonic.Client.ISubsonicClient client,
        LyricsQuery query,
        bool structuredDefinitive,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetLyricsAsync(query.Artist, query.Title, cancellationToken).ConfigureAwait(false);
            if (response.Status == "failed")
            {
                var kind = SubsonicLyricsClassifier.ClassifyFailedStatus(response.Error?.Code ?? 0);
                if (kind == LyricsOutcomeKind.Transient)
                {
                    _log.Warning("Legacy lyrics fetch failed (transient)", new { query.Artist, query.Title, Code = response.Error?.Code ?? 0 });
                }

                return (null, kind);
            }

            var text = response.Lyrics?.Value;
            if (string.IsNullOrWhiteSpace(text))
            {
                return (null, LyricsOutcomeKind.DefinitiveEmpty);
            }

            var payload = Encoding.UTF8.GetBytes(text);
            var document = _parserRegistry.Parse(LyricsFormat.PlainText, payload);
            if (document is null)
            {
                return (null, LyricsOutcomeKind.DefinitiveEmpty);
            }

            // Legacy content is cacheable only when structured was definitive (a transient structured
            // result might have produced a better answer once it clears).
            var content = LyricsProviderResult.Content(
                document.WithSourceProvider(Id), payload, LyricsFormat.PlainText, definitive: structuredDefinitive);
            return (content, LyricsOutcomeKind.Content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — do not mask it as a provider transient
        }
        catch (Exception ex)
        {
            // Includes HTTP timeouts (TaskCanceledException without our token) → transient.
            _log.Warning("Legacy lyrics fetch threw", new { query.Artist, query.Title }, ex);
            return (null, LyricsOutcomeKind.Transient);
        }
    }

    /// <summary>
    /// Picks the best <see cref="StructuredLyrics"/> entry across the (possibly multiple) entries a
    /// server returns. Each text-usable entry is serialized and parsed; the one whose document has the
    /// highest sync tier wins (so a synced entry beats a plain one even when the plain entry is listed
    /// first), with list order as the tiebreak. Returns the winner's payload + parsed document, or
    /// <c>null</c> when no entry yields a usable document.
    /// </summary>
    private (byte[] Payload, LyricsDocument Document)? SelectBestStructuredContent(StructuredLyrics[]? entries)
    {
        if (entries is null)
        {
            return null;
        }

        (byte[] Payload, LyricsDocument Document)? best = null;
        foreach (var entry in entries)
        {
            if (entry?.Line is null)
            {
                continue;
            }

            if (!entry.Line.Any(line => line is { Value: { } value } && !string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            var payload = JsonSerializer.SerializeToUtf8Bytes(entry, Json.CompactOptions);
            var document = _parserRegistry.Parse(LyricsFormat.SubsonicStructured, payload);
            if (document is null)
            {
                continue;
            }

            // Strict '>' keeps the earliest entry on a tie — list order is the tiebreak.
            if (best is null || LyricsSyncKindRank.Of(document.SyncKind) > LyricsSyncKindRank.Of(best.Value.Document.SyncKind))
            {
                best = (payload, document);
            }
        }

        return best;
    }
}
