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

        _log.Debug($"Searching {platform} lyrics", new { query.Title, query.Artist, query.Album, query.DurationSeconds });

        var metadata = new TrackMultiArtistMetadata
        {
            Title = query.Title,
            Artist = query.Artist,
            Album = query.Album,
            DurationMs = (query.DurationSeconds ?? 0) * 1000,
        };

        var result = await SearchHelper.Search(metadata, searcher,
            Lyricify.Lyrics.Searchers.Helpers.CompareHelper.MatchType.NoMatch).ConfigureAwait(false);
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

    private static async Task<(string? Raw, string FormatTag)> FetchRawAsync(
        LyricifyPlatform platform, ISearchResult result, CancellationToken cancellationToken)
    {
        switch (platform)
        {
            case LyricifyPlatform.QQMusic when result is QQMusicSearchResult qq:
            {
                var response = await ProviderHelper.QQMusicApi.GetLyricsAsync(qq.Id).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return (response?.Lyrics, QQLyricsConstants.QrcFormat);
            }
            case LyricifyPlatform.Netease when result is NeteaseSearchResult ne:
            {
                // GetLyricNew hits the eapi/song/lyric/v1 endpoint, which returns word-level YRC (yv/ytv/yrv).
                // The older GetLyric hits weapi/song/lyric and returns LRC only — it never yields YRC, so it
                // must NOT be used here or Netease silently degrades to line-tier.
                var response = await ProviderHelper.NeteaseApi.GetLyricNew(ne.Id).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var yrc = response?.Yrc?.Lyric;
                return !string.IsNullOrWhiteSpace(yrc)
                    ? (yrc, NeteaseLyricsConstants.YrcFormat)
                    : (response?.Lrc?.Lyric, NeteaseLyricsConstants.LrcFormat);
            }
            case LyricifyPlatform.Kugou when result is KugouSearchResult kg:
            {
                var response = await ProviderHelper.KugouApi.GetSearchLyrics(hash: kg.Hash).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = response?.Candidates?.FirstOrDefault();
                if (candidate is null)
                {
                    return (null, KugouLyricsConstants.KrcFormat);
                }

                var krc = await Lyricify.Lyrics.Decrypter.Krc.Helper
                    .GetLyricsAsync(candidate.Id, candidate.AccessKey).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return (krc, KugouLyricsConstants.KrcFormat);
            }
            default:
                return (null, string.Empty);
        }
    }
}
