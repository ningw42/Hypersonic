using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Requests;
using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Client;

public interface ISubsonicClient
{
    #region System

    public Task<SubsonicResponse> PingAsync();

    public Task<GetLicenseResponse> GetLicenseAsync();

    public Task<GetOpenSubsonicExtensionsResponse> GetOpenSubsonicExtensions();

    #endregion System

    #region Browsing

    public Task<GetMusicFoldersResponse> GetMusicFoldersAsync();
    
    public Task<GetIndexesResponse> GetIndexesAsync(string? musicFolderId = null, ulong? ifModifiedSince = null);

    public Task<GetMusicDirectoryResponse> GetMusicDirectoryAsync(string id);

    public Task<GetGenresResponse> GetGenresAsync();

    public Task<GetArtistsResponse> GetArtistsAsync(string? musicFolderId = null);

    public Task<GetArtistResponse> GetArtistAsync(string id);

    public Task<GetAlbumResponse> GetAlbumAsync(string id);

    public Task<GetSongResponse> GetSongAsync(string id);

    public Task<GetArtistInfoResponse> GetArtistInfoAsync(string id, int count = 20, bool includeNotPresent = false);

    public Task<GetArtistInfo2Response> GetArtistInfo2Async(string id, int count = 20, bool includeNotPresent = false);

    public Task<GetAlbumInfoResponse> GetAlbumInfoAsync(string id);

    public Task<GetAlbumInfo2Response> GetAlbumInfo2Async(string id);

    public Task<GetSimilarSongsResponse> GetSimiarSongsAsync(string id, int count = 50);

    public Task<GetSimilarSongs2Response> GetSimiarSongs2Async(string id, int count = 50);

    public Task<GetTopSongsResponse> GetTopSongsAsync(string artist, int count = 50);

    #endregion Browsing

    #region Album/Song Lists

    public Task<GetAlbumListResponse> GetAlbumListAsync(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null);    public Task<GetAlbumList2Response> GetAlbumList2Async(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null);

    public Task<GetRandomSongsResponse> GetRandomSongsAsync(int? size = null, string? genre = null, int? fromYear = null, int? toYear = null, string? musicFolderId = null);

    public Task<GetSongsByGenreResponse> GetSongsByGenreAsync(string genre, int? count = null, int? offset = null, string? musicFolderId = null);

    public Task<GetNowPlayingResponse> GetNowPlayingAsync();

    public Task<GetStarredResponse> GetStarredAsync(string? musicFolderId = null);

    public Task<GetStarred2Response> GetStarred2Async(string? musicFolderId = null);

    #endregion Album/Song Lists

    #region Searching

    public Task<Search2Response> Search2Async(string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null);

    public Task<Search3Response> Search3Async(string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null);

    #endregion Searching

    #region Playlists

    public Task<GetPlaylistsResponse> GetPlaylistsAsync(string? username = null);

    public Task<GetPlaylistResponse> GetPlaylistAsync(string id);

    public Task<SubsonicResponse> CreatePlaylistAsync(string? playlistId = null, string? name = null, string[]? songId = null);

    public Task<SubsonicResponse> UpdatePlaylistAsync(string playlistId, string? name = null, string? comment = null, bool? @public = null, string[]? songIdToAdd = null, int[]? songIndexToRemove = null);

    public Task<SubsonicResponse> DeletePlaylistAsync(string id);

    #endregion Playlists

    #region Media Retrieval

    public Task<Stream> StreamAsync(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false);

    public Uri GetStreamUri(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false);

    public Task<Stream> DownloadAsync(string id);

    public Task<Stream> GetCoverArtAsync(string id, string? size = null);

    public Uri GetCoverArtUri(string id, string? size = null);

    public Task<GetLyricsResponse> GetLyricsAsync(string? artist = null, string? title = null);

    public Task<GetLyricsBySongIdResponse> GetLyricsBySongIdAsync(string id);

    public Task<Stream> GetAvatarAsync(string username);

    public Uri GetAvatarUri(string username);

    #endregion Media Retrieval

    #region Media Annotation

    public Task<SubsonicResponse> StarAsync(string? id = null, string? albumId = null, string? artistId = null);

    public Task<SubsonicResponse> UnstarAsync(string? id = null, string? albumId = null, string? artistId = null);

    public Task<SubsonicResponse> SetRatingAsync(string id, int rating);

    public Task<SubsonicResponse> ScrobbleAsync(string id, ulong? time = null, bool submission = true);

    #endregion Media Annotation

    #region Sharing
    #endregion Sharing

    #region Podcast
    #endregion Podcast

    #region Jukebox
    #endregion Jukebox

    #region Internet Ratio
    #endregion Internet Ratio

    #region Chat
    #endregion Chat

    #region User Management

    public Task<GetUserResponse> GetUserAsync(string username);
    public Task<GetUsersResponse> GetUsersAsync();

    #endregion User Management

    #region Bookmarks
    #endregion Bookmarks

    #region Media Library Scanning

    public Task<GetScanStatusResponse> GetScanStatusAsync();
    public Task<StartScanResponse> StartScanAsync();

    #endregion Media Library Scanning

    #region OpenSubsonic
    #endregion OpenSubsonic
}

public enum ResponseFormat
{
    XML,
    JSON,
    JSONP, // not implemented
}

public class SubsonicClient : ISubsonicClient
{
    public HttpClient HttpClient { get; private set; }

    public string BaseUri { get; private set; }

    public string User { get; private set; }

    public string Password { get; private set; }

    public string ClientVersion { get; private set; }

    public string ClientId { get; private set; }

    public ResponseFormat ResponseFormat { get; private set; }

    private SubsonicRequest baseRequest = null;

    private SubsonicRequest BaseRequest
    {
        get
        {
            this.baseRequest ??= new SubsonicRequest(
                    user: this.User,
                    password: this.Password,
                    clientId: this.ClientId,
                    clientVersion: this.ClientVersion,
                    responseFormat: this.ResponseFormat);

            return this.baseRequest;
        }
    }

    public SubsonicClient(HttpClient client, string baseUri, string user, string password, string clientVersion, string clientId, ResponseFormat responseFormat = ResponseFormat.XML)
    {
        this.HttpClient = client;
        this.BaseUri = baseUri;
        this.User = user;
        this.Password = password;
        this.ClientVersion = clientVersion;
        this.ClientId = clientId;
        this.ResponseFormat = responseFormat;
    }

    #region System Endpoints

    public async Task<SubsonicResponse> PingAsync()
    {
        return await this.MakeSubsonicRequestAsync<PingRequest, SubsonicResponse>(new PingRequest(this.BaseRequest));
    }

    public async Task<GetLicenseResponse> GetLicenseAsync()
    {
        return await this.MakeSubsonicRequestAsync<GetLicenseRequest, GetLicenseResponse>(new GetLicenseRequest(this.BaseRequest));
    }

    public async Task<GetOpenSubsonicExtensionsResponse> GetOpenSubsonicExtensions()
    {
        return await this.MakeSubsonicRequestAsync<GetOpenSubsonicExtensionsRequest, GetOpenSubsonicExtensionsResponse>(new GetOpenSubsonicExtensionsRequest(this.BaseRequest));
    }

    #endregion System Endpoints

    #region Browsing Endpoints

    public async Task<GetMusicFoldersResponse> GetMusicFoldersAsync()
    {
        return await this.MakeSubsonicRequestAsync<GetMusicFoldersRequest, GetMusicFoldersResponse>(new GetMusicFoldersRequest(this.BaseRequest));
    }

    public async Task<GetIndexesResponse> GetIndexesAsync(string? musicFolderId = null, ulong? ifModifiedSince = null)
    {
        return await this.MakeSubsonicRequestAsync<GetIndexesRequest, GetIndexesResponse>(new GetIndexesRequest(this.BaseRequest, musicFolderId, ifModifiedSince));
    }

    public async Task<GetMusicDirectoryResponse> GetMusicDirectoryAsync(string id)
    {
        return await this.MakeSubsonicRequestAsync<GetMusicDirectoryRequest, GetMusicDirectoryResponse>(new GetMusicDirectoryRequest(this.BaseRequest, id));
    }

    public async Task<GetGenresResponse> GetGenresAsync()
    {
        return await this.MakeSubsonicRequestAsync<GetGenresRequest, GetGenresResponse>(new GetGenresRequest(this.BaseRequest));
    }

    public async Task<GetArtistsResponse> GetArtistsAsync(string? musicFolderId = null)
    {
        return await this.MakeSubsonicRequestAsync<GetArtistsRequest, GetArtistsResponse>(new GetArtistsRequest(this.BaseRequest, musicFolderId));
    }

    public async Task<GetArtistResponse> GetArtistAsync(string id)
    {
        return await this.MakeSubsonicRequestAsync<GetArtistRequest, GetArtistResponse>(new GetArtistRequest(this.BaseRequest, id));
    }

    public async Task<GetAlbumResponse> GetAlbumAsync(string id)
    {
        return await this.MakeSubsonicRequestAsync<GetAlbumRequest, GetAlbumResponse>(new GetAlbumRequest(this.BaseRequest, id));
    }

    public async Task<GetSongResponse> GetSongAsync(string id)
    {
        return await this.MakeSubsonicRequestAsync<GetSongRequest, GetSongResponse>(new GetSongRequest(this.BaseRequest, id));
    }

    public async Task<GetArtistInfoResponse> GetArtistInfoAsync(string id, int count = 20, bool includeNotPresent = false)
    {
        return await this.MakeSubsonicRequestAsync<GetArtistInfoRequest, GetArtistInfoResponse>(new GetArtistInfoRequest(this.BaseRequest, id, count, includeNotPresent));
    }

    public async Task<GetArtistInfo2Response> GetArtistInfo2Async(string id, int count = 20, bool includeNotPresent = false)
    {
        return await this.MakeSubsonicRequestAsync<GetArtistInfo2Request, GetArtistInfo2Response>(new GetArtistInfo2Request(this.BaseRequest, id, count, includeNotPresent));
    }

    public async Task<GetAlbumInfoResponse> GetAlbumInfoAsync(string id)
    {
        return await this.MakeSubsonicRequestAsync<GetAlbumInfoRequest, GetAlbumInfoResponse>(new GetAlbumInfoRequest(this.BaseRequest, id));
    }

    public async Task<GetAlbumInfo2Response> GetAlbumInfo2Async(string id)
    {
        return await this.MakeSubsonicRequestAsync<GetAlbumInfo2Request, GetAlbumInfo2Response>(new GetAlbumInfo2Request(this.BaseRequest, id));
    }

    public async Task<GetSimilarSongsResponse> GetSimiarSongsAsync(string id, int count = 50)
    {
        return await this.MakeSubsonicRequestAsync<GetSimilarSongsRequest, GetSimilarSongsResponse>(new GetSimilarSongsRequest(this.BaseRequest, id, count));
    }

    public async Task<GetSimilarSongs2Response> GetSimiarSongs2Async(string id, int count = 50)
    {
        return await this.MakeSubsonicRequestAsync<GetSimilarSongs2Request, GetSimilarSongs2Response>(new GetSimilarSongs2Request(this.BaseRequest, id, count));
    }

    public async Task<GetTopSongsResponse> GetTopSongsAsync(string artist, int count = 50)
    {
        return await this.MakeSubsonicRequestAsync<GetTopSongsRequest, GetTopSongsResponse>(new GetTopSongsRequest(this.BaseRequest, artist, count));
    }

    #endregion Browsing Endpoints

    #region Album/Song lists

    public async Task<GetAlbumListResponse> GetAlbumListAsync(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null)
    {
        return await this.MakeSubsonicRequestAsync<GetAlbumListRequest, GetAlbumListResponse>(new GetAlbumListRequest(this.BaseRequest, type, size, offset, fromYear, toYear, genre, musicFolderId));
    }

    public async Task<GetAlbumList2Response> GetAlbumList2Async(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null)
    {
        return await this.MakeSubsonicRequestAsync<GetAlbumList2Request, GetAlbumList2Response>(new GetAlbumList2Request(this.BaseRequest, type, size, offset, fromYear, toYear, genre, musicFolderId));
    }

    public async Task<GetRandomSongsResponse> GetRandomSongsAsync(int? size = null, string? genre = null, int? fromYear = null, int? toYear = null, string? musicFolderId = null)
    {
        return await this.MakeSubsonicRequestAsync<GetRandomSongsRequest, GetRandomSongsResponse>(new GetRandomSongsRequest(this.BaseRequest, size, genre, fromYear, toYear, musicFolderId));
    }

    public async Task<GetSongsByGenreResponse> GetSongsByGenreAsync(string genre, int? count = null, int? offset = null, string? musicFolderId = null)
    {
        return await this.MakeSubsonicRequestAsync<GetSongsByGenreRequest, GetSongsByGenreResponse>(new GetSongsByGenreRequest(this.BaseRequest, genre, count, offset, musicFolderId));
    }

    public async Task<GetNowPlayingResponse> GetNowPlayingAsync()
    {
        return await this.MakeSubsonicRequestAsync<GetNowPlayingRequest, GetNowPlayingResponse>(new GetNowPlayingRequest(this.BaseRequest));
    }

    public async Task<GetStarredResponse> GetStarredAsync(string? musicFolderId = null)
    {
        return await this.MakeSubsonicRequestAsync<GetStarredRequest, GetStarredResponse>(new GetStarredRequest(this.BaseRequest, musicFolderId));
    }

    public async Task<GetStarred2Response> GetStarred2Async(string? musicFolderId = null)
    {
        return await this.MakeSubsonicRequestAsync<GetStarred2Request, GetStarred2Response>(new GetStarred2Request(this.BaseRequest, musicFolderId));    }

    #endregion Album/Song lists

    #region Searching

    public async Task<Search2Response> Search2Async(string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null)
    {
        return await this.MakeSubsonicRequestAsync<Search2Request, Search2Response>(new Search2Request(this.BaseRequest, query, artistCount, artistOffset, albumCount, albumOffset, songCount, songOffset, musicFolderId));
    }

    public async Task<Search3Response> Search3Async(string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null)
    {
        return await this.MakeSubsonicRequestAsync<Search3Request, Search3Response>(
            new Search3Request(
                this.BaseRequest,
                query,
                artistCount,
                artistOffset,
                albumCount,
                albumOffset,
                songCount,
                songOffset,
                musicFolderId));
    }

    #endregion Searching

    #region Playlists

    public async Task<GetPlaylistsResponse> GetPlaylistsAsync(string? username = null)
    {
        return await this.MakeSubsonicRequestAsync<GetPlaylistsRequest, GetPlaylistsResponse>(new GetPlaylistsRequest(this.BaseRequest, username));
    }

    public async Task<GetPlaylistResponse> GetPlaylistAsync(string id)
    {
        return await this.MakeSubsonicRequestAsync<GetPlaylistRequest, GetPlaylistResponse>(new GetPlaylistRequest(this.BaseRequest, id));
    }

    public async Task<SubsonicResponse> CreatePlaylistAsync(string? playlistId = null, string? name = null, string[]? songId = null)
    {
        return await this.MakeSubsonicRequestAsync<CreatePlaylistRequest, SubsonicResponse>(new CreatePlaylistRequest(this.BaseRequest, playlistId, name, songId));
    }

    public async Task<SubsonicResponse> UpdatePlaylistAsync(string playlistId, string? name = null, string? comment = null, bool? @public = null, string[]? songIdToAdd = null, int[]? songIndexToRemove = null)
    {
        return await this.MakeSubsonicRequestAsync<UpdatePlaylistRequest, SubsonicResponse>(new UpdatePlaylistRequest(this.BaseRequest, playlistId, name, comment, @public, songIdToAdd, songIndexToRemove));
    }

    public async Task<SubsonicResponse> DeletePlaylistAsync(string id)
    {
        return await this.MakeSubsonicRequestAsync<DeletePlaylistRequest, SubsonicResponse>(new DeletePlaylistRequest(this.BaseRequest, id));
    }

    #endregion Playlists

    #region Media Retrieval

    public async Task<Stream> StreamAsync(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false)
    {
        return await this.MakeSubsonicRequestWithoutDeserializationAsync<StreamRequest>(
            new StreamRequest(
                this.BaseRequest,
                id,
                maxBitrate,
                format,
                timeOffset,
                size,
                estimateContentLength,
                converted));
    }

    public Uri GetStreamUri(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false)
    {
        StreamRequest request = new StreamRequest(
            this.BaseRequest,
            id,
            maxBitrate,
            format,
            timeOffset,
            size,
            estimateContentLength,
            converted);

        return this.GetUri(request);
    }

    public async Task<Stream> DownloadAsync(string id)
    {
        return await this.MakeSubsonicRequestWithoutDeserializationAsync<DownloadRequest>(new DownloadRequest(this.BaseRequest, id));
    }

    public async Task<Stream> GetCoverArtAsync(string id, string? size = null)
    {
        return await this.MakeSubsonicRequestWithoutDeserializationAsync<GetCoverArtRequest>(new GetCoverArtRequest(this.BaseRequest, id, size));
    }

    public Uri GetCoverArtUri(string id, string? size = null)
    {
        GetCoverArtRequest request = new GetCoverArtRequest(this.BaseRequest, id, size);
        return this.GetUri(request);
    }

    public async Task<GetLyricsResponse> GetLyricsAsync(string? artist = null, string? title = null)
    {
        return await this.MakeSubsonicRequestAsync<GetLyricsRequest, GetLyricsResponse>(new GetLyricsRequest(this.BaseRequest, artist, title));
    }

    public async Task<GetLyricsBySongIdResponse> GetLyricsBySongIdAsync(string id)
    {
        return await this.MakeSubsonicRequestAsync<GetLyricsBySongIdRequest, GetLyricsBySongIdResponse>(new GetLyricsBySongIdRequest(this.BaseRequest, id));
    }

    public async Task<Stream> GetAvatarAsync(string username)
    {
        return await this.MakeSubsonicRequestWithoutDeserializationAsync<GetAvatarRequest>(new GetAvatarRequest(this.BaseRequest, username));
    }

    public Uri GetAvatarUri(string username)
    {
        GetAvatarRequest request = new GetAvatarRequest(this.BaseRequest, username);
        return this.GetUri(request);
    }

    #endregion Media Retrieval

    #region Media Annotation

    public async Task<SubsonicResponse> StarAsync(string? id = null, string? albumId = null, string? artistId = null)
    {
        return await this.MakeSubsonicRequestAsync<StarRequest, SubsonicResponse>(new StarRequest(this.BaseRequest, id, albumId, artistId));
    }

    public async Task<SubsonicResponse> UnstarAsync(string? id = null, string? albumId = null, string? artistId = null)
    {
        return await this.MakeSubsonicRequestAsync<UnstarRequest, SubsonicResponse>(new UnstarRequest(this.BaseRequest, id, albumId, artistId));
    }

    public async Task<SubsonicResponse> SetRatingAsync(string id, int rating)
    {
        return await this.MakeSubsonicRequestAsync<SetRatingRequest, SubsonicResponse>(new SetRatingRequest(this.BaseRequest, id, rating));
    }

    public async Task<SubsonicResponse> ScrobbleAsync(string id, ulong? time = null, bool submission = true)
    {
        return await this.MakeSubsonicRequestAsync<ScrobbleRequest, SubsonicResponse>(new ScrobbleRequest(this.BaseRequest, id, time, submission));
    }

    #endregion Media Annotation

    #region Sharing
    #endregion Sharing

    #region Podcast
    #endregion Podcast

    #region Jukebox
    #endregion Jukebox

    #region Internet Ratio
    #endregion Internet Ratio

    #region Chat
    #endregion Chat

    #region User Management

    public async Task<GetUserResponse> GetUserAsync(string username)
    {
        return await this.MakeSubsonicRequestAsync<GetUserRequest, GetUserResponse>(new GetUserRequest(this.BaseRequest, username));
    }

    public async Task<GetUsersResponse> GetUsersAsync()
    {
        return await this.MakeSubsonicRequestAsync<GetUsersRequest, GetUsersResponse>(new GetUsersRequest(this.BaseRequest));
    }

    #endregion User Management

    #region Bookmarks
    #endregion Bookmarks

    #region Media Library Scanning

    public async Task<GetScanStatusResponse> GetScanStatusAsync()
    {
        return await this.MakeSubsonicRequestAsync<GetScanStatusRequest, GetScanStatusResponse>(new GetScanStatusRequest(this.BaseRequest));
    }

    public async Task<StartScanResponse> StartScanAsync()
    {
        return await this.MakeSubsonicRequestAsync<StartScanRequest, StartScanResponse>(new StartScanRequest(this.BaseRequest));
    }

    #endregion Media Library Scanning

    #region OpenSubsonic
    #endregion OpenSubsonic

    private Uri GetUri(SubsonicRequest request)
    {
        return new Uri($"{this.BaseUri}/{request.Action}{request.ToQueryString()}");
    }

    private async Task<TResponse> MakeSubsonicRequestAsync<TRequest, TResponse>(TRequest request) where TRequest : SubsonicRequest
    {
        Stream content = await this.MakeSubsonicRequestWithoutDeserializationAsync(request);

        return this.Deserialize<TResponse>(content);
    }

    private async Task<Stream> MakeSubsonicRequestWithoutDeserializationAsync<TRequest>(TRequest request) where TRequest : SubsonicRequest
    {
        Uri uri = this.GetUri(request);
        HttpResponseMessage response = await this.HttpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync();
    }

    private T Deserialize<T>(Stream stream)
    {
        try
        {
            return this.ResponseFormat switch
            {
                ResponseFormat.XML => SubsonicClient.DeserializeXmlResponse<T>(stream),
                ResponseFormat.JSON => SubsonicClient.DeserializeJsonResponse<T>(stream),
                _ => throw new NotSupportedException($"Unsupported response format: {this.ResponseFormat}")
            };
        }
        catch (Exception ex) when (ex is JsonException || ex is XmlException)
        {
            throw new InvalidOperationException("Failed to deserialize response", ex);
        }
        catch (Exception ex) when (ex is NotSupportedException || ex is NotImplementedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("An unexpected error occurred during deserialization", ex);
        }
    }

    public static T DeserializeXmlResponse<T>(Stream stream)
    {
        return SubsonicClient.DeserializeXml<T>(stream);
    }

    public static T DeserializeXml<T>(Stream stream)
    {
        XmlSerializer serializer = new XmlSerializer(typeof(T));
        using (XmlReader reader = new XmlTextReader(stream) { Namespaces = false })
        {
            return (T)serializer.Deserialize(reader);
        }
    }

    public static T DeserializeJsonResponse<T>(Stream stream)
    {
        JsonResponseWrapper<T> wrapper = JsonSerializer.Deserialize<JsonResponseWrapper<T>>(stream);
        return wrapper.SubsonicResponse;
    }

    public static T DeserializeJson<T>(Stream stream)
    {
        return JsonSerializer.Deserialize<T>(stream);
    }
}
