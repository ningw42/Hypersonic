using System.Text;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Shared body for the three search-based CN providers. Honors <c>Enabled</c>, guards a blank title,
/// calls the facade, gates the candidate on <see cref="LyricsMatchScorer"/>, and parses the raw payload
/// through the shared registry. Subclasses supply only the platform, id, and options.
/// </summary>
internal abstract class LyricifyLyricsProviderBase : ILyricsProvider
{
    private readonly ILyricifyLyricsClient _client;
    private readonly ILyricsParserRegistry _parserRegistry;
    private readonly ILogger _log;

    protected LyricifyLyricsProviderBase(ILyricifyLyricsClient client, ILyricsParserRegistry parserRegistry, ILoggingService logService)
    {
        _client = client;
        _parserRegistry = parserRegistry;
        _log = logService.GetLogger(GetType().Name);
    }

    public abstract string Id { get; }
    protected abstract LyricifyPlatform Platform { get; }
    protected abstract bool Enabled { get; }
    protected abstract int MatchThreshold { get; }

    public async Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Enabled)
        {
            return LyricsProviderResult.DefinitiveEmpty; // inert but non-blocking to the negative cache
        }

        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return LyricsProviderResult.Transient; // nothing queryable; do not negative-cache
        }

        LyricifyLyricsResult? result;
        try
        {
            result = await _client.FetchAsync(Platform, query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — never masked
        }
        catch (Exception ex)
        {
            _log.Warning("Lyricify fetch failed (transient)", new { Id, query.SongId }, ex);
            return LyricsProviderResult.Transient;
        }

        if (result is null)
        {
            return LyricsProviderResult.DefinitiveEmpty; // no candidate from this source
        }

        var score = LyricsMatchScorer.Score(query, result.Candidate);
        if (score < MatchThreshold)
        {
            return LyricsProviderResult.DefinitiveEmpty; // not confident enough — don't hand a wrong-song match to the policy
        }

        var bytes = Encoding.UTF8.GetBytes(result.RawText);
        var document = _parserRegistry.Parse(result.FormatTag, bytes);
        if (document is null)
        {
            return LyricsProviderResult.Transient; // present-but-unparseable → never assert "no lyrics"
        }

        return LyricsProviderResult.Content(document, bytes, result.FormatTag, definitive: true);
    }
}
