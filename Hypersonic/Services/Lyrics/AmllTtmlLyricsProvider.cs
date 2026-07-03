using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// The amll-ttml-db lyrics source. Thin: it delegates matching to <see cref="IAmllLyricsIndex"/>,
/// GETs the matched raw TTML from the CDN, and parses it through the shared registry (word-synced,
/// top tier). All matching/index policy lives in the index; all TTML parsing in the shared parser.
/// </summary>
internal sealed class AmllTtmlLyricsProvider : ILyricsProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAmllLyricsIndex _index;
    private readonly ILyricsParserRegistry _parserRegistry;
    private readonly AmllLyricsOptions _options;
    private readonly ILogger _log;

    public AmllTtmlLyricsProvider(
        IHttpClientFactory httpClientFactory,
        IAmllLyricsIndex index,
        ILyricsParserRegistry parserRegistry,
        IOptions<AmllLyricsOptions> options,
        ILoggingService logService)
    {
        _httpClientFactory = httpClientFactory;
        _index = index;
        _parserRegistry = parserRegistry;
        _options = options.Value;
        _log = logService.GetLogger(nameof(AmllTtmlLyricsProvider));
    }

    public string Id => AmllLyricsConstants.ProviderId;

    public async Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Disabled → inert but non-blocking to the negative cache (a permanent Transient would block it).
        if (!_options.Enabled)
        {
            return LyricsProviderResult.DefinitiveEmpty;
        }

        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return LyricsProviderResult.Transient; // nothing queryable; do not negative-cache
        }

        if (!_index.IsReady)
        {
            return LyricsProviderResult.Transient; // warming — retried next track, no negative poisoning
        }

        var file = _index.TryResolve(query);
        if (file is null)
        {
            return LyricsProviderResult.DefinitiveEmpty; // this source honestly has no entry
        }

        try
        {
            var client = _httpClientFactory.CreateClient(AmllLyricsConstants.HttpClientName);
            using var response = await client.GetAsync(AmllLyricsConstants.PayloadPathPrefix + file, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return LyricsProviderResult.Transient;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var document = _parserRegistry.Parse(TtmlLyricsConstants.Format, bytes);
            if (document is null)
            {
                // Present-but-unparseable payload → parse failure → Transient (never assert "no lyrics").
                return LyricsProviderResult.Transient;
            }

            return LyricsProviderResult.Content(document, bytes, TtmlLyricsConstants.Format, definitive: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — never masked as a provider transient
        }
        catch (Exception ex)
        {
            _log.Warning("AMLL payload fetch failed (transient)", new { File = file, query.SongId }, ex);
            return LyricsProviderResult.Transient;
        }
    }
}
