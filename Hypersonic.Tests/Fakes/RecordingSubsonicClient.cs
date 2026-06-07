using Hypersonic.Client;
using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Tests.Fakes;

/// <summary>
/// Minimal ISubsonicClient test double that records Star/Unstar calls and can
/// be forced to throw. All other members throw NotImplementedException.
/// </summary>
public sealed class RecordingSubsonicClient : ISubsonicClient
{
    public bool StarShouldThrow { get; set; }
    public int StarCallCount { get; private set; }
    public int UnstarCallCount { get; private set; }
    public string? LastStarredId { get; private set; }
    public string? LastUnstarredId { get; private set; }

    /// <summary>Status returned in the SubsonicResponse from Star/Unstar ("ok" or "failed").</summary>
    public string ResponseStatus { get; set; } = "ok";

    /// <summary>When set, Star/Unstar await this gate before completing — lets a test
    /// interleave a track change mid-request to exercise the revert race.</summary>
    public TaskCompletionSource<bool>? Gate { get; set; }

    public async Task<SubsonicResponse> StarAsync(string? id = null, string? albumId = null, string? artistId = null)
    {
        if (Gate is not null) await Gate.Task;
        if (StarShouldThrow) throw new HttpRequestException("Star failed");
        StarCallCount++;
        LastStarredId = id;
        return new SubsonicResponse { Status = ResponseStatus };
    }

    public async Task<SubsonicResponse> UnstarAsync(string? id = null, string? albumId = null, string? artistId = null)
    {
        if (Gate is not null) await Gate.Task;
        if (StarShouldThrow) throw new HttpRequestException("Unstar failed");
        UnstarCallCount++;
        LastUnstarredId = id;
        return new SubsonicResponse { Status = ResponseStatus };
    }

    // --- All other ISubsonicClient members: unused stubs. ---
    public Uri GetCoverArtUri(string id, string? size = null) => new($"https://test/cover/{id}");
    public Uri GetStreamUri(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false) => new($"https://test/stream/{id}");

    public Task<SubsonicResponse> PingAsync() => throw new NotImplementedException();
    public Task<GetLicenseResponse> GetLicenseAsync() => throw new NotImplementedException();
    public Task<GetOpenSubsonicExtensionsResponse> GetOpenSubsonicExtensions() => throw new NotImplementedException();
    public Task<GetMusicFoldersResponse> GetMusicFoldersAsync() => throw new NotImplementedException();
    public Task<GetIndexesResponse> GetIndexesAsync(string? musicFolderId = null, ulong? ifModifiedSince = null) => throw new NotImplementedException();
    public Task<GetMusicDirectoryResponse> GetMusicDirectoryAsync(string id) => throw new NotImplementedException();
    public Task<GetGenresResponse> GetGenresAsync() => throw new NotImplementedException();
    public Task<GetArtistsResponse> GetArtistsAsync(string? musicFolderId = null) => throw new NotImplementedException();
    public Task<GetArtistResponse> GetArtistAsync(string id) => throw new NotImplementedException();
    public Task<GetAlbumResponse> GetAlbumAsync(string id) => throw new NotImplementedException();
    public Task<GetSongResponse> GetSongAsync(string id) => throw new NotImplementedException();
    public Task<GetArtistInfoResponse> GetArtistInfoAsync(string id, int count = 20, bool includeNotPresent = false) => throw new NotImplementedException();
    public Task<GetArtistInfo2Response> GetArtistInfo2Async(string id, int count = 20, bool includeNotPresent = false) => throw new NotImplementedException();
    public Task<GetAlbumInfoResponse> GetAlbumInfoAsync(string id) => throw new NotImplementedException();
    public Task<GetAlbumInfo2Response> GetAlbumInfo2Async(string id) => throw new NotImplementedException();
    public Task<GetSimilarSongsResponse> GetSimiarSongsAsync(string id, int count = 50) => throw new NotImplementedException();
    public Task<GetSimilarSongs2Response> GetSimiarSongs2Async(string id, int count = 50) => throw new NotImplementedException();
    public Task<GetTopSongsResponse> GetTopSongsAsync(string artist, int count = 50) => throw new NotImplementedException();
    public Task<GetAlbumListResponse> GetAlbumListAsync(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null) => throw new NotImplementedException();
    public Task<GetAlbumList2Response> GetAlbumList2Async(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null) => throw new NotImplementedException();
    public Task<GetRandomSongsResponse> GetRandomSongsAsync(int? size = null, string? genre = null, int? fromYear = null, int? toYear = null, string? musicFolderId = null) => throw new NotImplementedException();
    public Task<GetSongsByGenreResponse> GetSongsByGenreAsync(string genre, int? count = null, int? offset = null, string? musicFolderId = null) => throw new NotImplementedException();
    public Task<GetNowPlayingResponse> GetNowPlayingAsync() => throw new NotImplementedException();
    public Task<GetStarredResponse> GetStarredAsync(string? musicFolderId = null) => throw new NotImplementedException();
    public Task<GetStarred2Response> GetStarred2Async(string? musicFolderId = null) => throw new NotImplementedException();
    public Task<Search2Response> Search2Async(string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null) => throw new NotImplementedException();
    public Task<Search3Response> Search3Async(string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null) => throw new NotImplementedException();
    public Task<GetPlaylistsResponse> GetPlaylistsAsync(string? username = null) => throw new NotImplementedException();
    public Task<GetPlaylistResponse> GetPlaylistAsync(string id) => throw new NotImplementedException();
    public Task<SubsonicResponse> CreatePlaylistAsync(string? playlistId = null, string? name = null, string[]? songId = null) => throw new NotImplementedException();
    public Task<SubsonicResponse> UpdatePlaylistAsync(string playlistId, string? name = null, string? comment = null, bool? @public = null, string[]? songIdToAdd = null, int[]? songIndexToRemove = null) => throw new NotImplementedException();
    public Task<SubsonicResponse> DeletePlaylistAsync(string id) => throw new NotImplementedException();
    public Task<Stream> StreamAsync(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false) => throw new NotImplementedException();
    public Task<Stream> DownloadAsync(string id) => throw new NotImplementedException();
    public Task<Stream> GetCoverArtAsync(string id, string? size = null) => throw new NotImplementedException();
    public Task<GetLyricsResponse> GetLyricsAsync(string? artist = null, string? title = null) => throw new NotImplementedException();
    public Task<SubsonicResponse> SetRatingAsync(string id, int rating) => throw new NotImplementedException();
    public Task<SubsonicResponse> ScrobbleAsync(string id, ulong? time = null, bool submission = true) => throw new NotImplementedException();
    public Task<GetUserResponse> GetUserAsync(string username) => throw new NotImplementedException();
    public Task<GetUsersResponse> GetUsersAsync() => throw new NotImplementedException();
    public Task<GetScanStatusResponse> GetScanStatusAsync() => throw new NotImplementedException();
    public Task<StartScanResponse> StartScanAsync() => throw new NotImplementedException();
    public Task<GetLyricsBySongIdResponse> GetLyricsBySongIdAsync(string id) => throw new NotImplementedException();
    public Task<Stream> GetAvatarAsync(string username) => throw new NotImplementedException();
    public Uri GetAvatarUri(string username) => throw new NotImplementedException();
}
