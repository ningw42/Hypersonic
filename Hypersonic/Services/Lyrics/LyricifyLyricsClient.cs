using System.Linq;

using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Searchers;

namespace Hypersonic.Services.Lyrics;

/// <summary>Concrete <see cref="ILyricifyLyricsClient"/> over the Lyricify library.</summary>
internal sealed class LyricifyLyricsClient : ILyricifyLyricsClient
{
    private readonly ILogger _log;

    public LyricifyLyricsClient(ILoggingService logService)
        => _log = logService.GetLogger(nameof(LyricifyLyricsClient));

    public async Task<LyricifyLyricsResult?> FetchAsync(LyricifyPlatform platform, LyricsQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var searcher = platform switch
        {
            LyricifyPlatform.QQMusic => Searchers.QQMusic,
            LyricifyPlatform.Netease => Searchers.Netease,
            LyricifyPlatform.Kugou => Searchers.Kugou,
            _ => Searchers.QQMusic,
        };

        _log.Debug($"Searching {platform} lyrics", new
        {
            HasTitle = !string.IsNullOrWhiteSpace(query.Title),
            HasArtist = !string.IsNullOrWhiteSpace(query.Artist),
            HasAlbum = !string.IsNullOrWhiteSpace(query.Album),
            query.DurationSeconds,
        });

        var metadata = new TrackMultiArtistMetadata
        {
            Title = query.Title,
            Artist = query.Artist,
            Album = query.Album,
            DurationMs = (query.DurationSeconds ?? 0) * 1000,
        };

        // WaitAsync releases the await once the token trips; the underlying request still runs
        // to completion (the library takes no CancellationToken) but its result is abandoned.
        var result = await SearchHelper.Search(metadata, searcher,
            Lyricify.Lyrics.Searchers.Helpers.CompareHelper.MatchType.NoMatch)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is null)
        {
            return null;
        }

        var candidate = new LyricsCandidate(
            result.Title, result.Artist, result.Album,
            result.DurationMs > 0 ? result.DurationMs / 1000 : null);

        var (rawText, formatTag) = await FetchRawAsync(platform, result, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        return new LyricifyLyricsResult(rawText!, formatTag, candidate);
    }

    private async Task<(string? Raw, string FormatTag)> FetchRawAsync(
        LyricifyPlatform platform, ISearchResult result, CancellationToken cancellationToken)
    {
        switch (platform)
        {
            case LyricifyPlatform.QQMusic when result is QQMusicSearchResult qq:
            {
                var response = await ProviderHelper.QQMusicApi.GetLyricsAsync(qq.Id)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return (response?.Lyrics, QQLyricsConstants.QrcFormat);
            }
            case LyricifyPlatform.Netease when result is NeteaseSearchResult ne:
            {
                // GetLyricNew hits the eapi/song/lyric/v1 endpoint, which returns word-level YRC (yv/ytv/yrv).
                // The older GetLyric hits weapi/song/lyric and returns LRC only — it never yields YRC, so it
                // must NOT be used here or Netease silently degrades to line-tier.
                var response = await ProviderHelper.NeteaseApi.GetLyricNew(ne.Id)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var yrc = response?.Yrc?.Lyric;
                return !string.IsNullOrWhiteSpace(yrc)
                    ? (yrc, NeteaseLyricsConstants.YrcFormat)
                    : (response?.Lrc?.Lyric, NeteaseLyricsConstants.LrcFormat);
            }
            case LyricifyPlatform.Kugou when result is KugouSearchResult kg:
            {
                var response = await ProviderHelper.KugouApi.GetSearchLyrics(hash: kg.Hash)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = response?.Candidates?.FirstOrDefault();
                if (candidate is null)
                {
                    // Legitimate "no lyrics for this hash" — a search hit with no lyric candidates.
                    _log.Debug("Kugou search hit had no lyric candidates");
                    return (null, KugouLyricsConstants.KrcFormat);
                }

                var krc = await Lyricify.Lyrics.Decrypter.Krc.Helper
                    .GetLyricsAsync(candidate.Id, candidate.AccessKey)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return (krc, KugouLyricsConstants.KrcFormat);
            }
            default:
                // The search result subtype doesn't match the requested platform — a shape the library
                // is not expected to produce. Treat as library rot: throw so the provider maps it to
                // Transient (retryable) rather than silently negative-caching the track as "no lyrics".
                _log.Warning("Unexpected search-result shape for platform",
                    new { Platform = platform.ToString(), ResultType = result.GetType().Name });
                throw new InvalidOperationException(
                    $"Unexpected search-result shape '{result.GetType().Name}' for platform '{platform}'.");
        }
    }
}
