using Hypersonic.Client;
using Hypersonic.Client.Models.Entities;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;

namespace Hypersonic.Tests.Helpers;

[TestClass]
public class SongMapperTests
{
    private sealed class FakeSubsonicClient : ISubsonicClient
    {
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
        public Task<SubsonicResponse> StarAsync(string? id = null, string? albumId = null, string? artistId = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> UnstarAsync(string? id = null, string? albumId = null, string? artistId = null) => throw new NotImplementedException();
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

    [TestMethod]
    public void MapFromChild_PreservesAllFields()
    {
        var client = new FakeSubsonicClient();
        var child = new Child
        {
            Id = "s1",
            Title = "Test Song",
            Artist = "Test Artist",
            ArtistId = "a1",
            Album = "Test Album",
            AlbumId = "alb1",
            Duration = 300,
            Track = 5,
            DiscNumber = 2,
            Genre = "Rock",
            Year = 2024,
            CoverArt = "cover1",
            BitRate = 320,
            Suffix = "mp3",
            Size = 12345678,
            Path = "/music/test.mp3",
            DisplayArtist = "Test Artist",
            PlayCount = 42,
            BPM = 120,
            Starred = "2024-01-01",
        };

        var result = SongMapper.MapFromChild(child, client);

        Assert.AreEqual("s1", result.Id);
        Assert.AreEqual("Test Song", result.Title);
        Assert.AreEqual("Test Artist", result.Artist);
        Assert.AreEqual("a1", result.ArtistId);
        Assert.AreEqual("Test Album", result.Album);
        Assert.AreEqual("alb1", result.AlbumId);
        Assert.AreEqual(300, result.Duration);
        Assert.AreEqual(5, result.TrackNumber);
        Assert.AreEqual(2, result.DiscNumber);
        Assert.AreEqual("Rock", result.Genre);
        Assert.AreEqual(2024, result.Year);
        Assert.AreEqual(320, result.BitRate);
        Assert.AreEqual("mp3", result.Suffix);
        Assert.AreEqual(12345678, result.Size);
        Assert.AreEqual("/music/test.mp3", result.Path);
        Assert.AreEqual("Test Artist", result.DisplayArtist);
        Assert.AreEqual(42, result.PlayCount);
        Assert.AreEqual(120, result.BPM);
        Assert.IsTrue(result.IsStarred);
        Assert.IsNotNull(result.StreamUri);
        Assert.IsNotNull(result.CoverImage);
        Assert.AreEqual(ImageReference.SubsonicProvider, result.CoverImage.Provider);
        Assert.AreEqual(ImageReference.CoverArtKind, result.CoverImage.Kind);
        Assert.AreEqual("cover1", result.CoverImage.Identity);
    }

    [TestMethod]
    public void MapFromChild_NullAlbumVersion_PassesRawAlbumName()
    {
        var client = new FakeSubsonicClient();
        var child = new Child { Id = "s1", Album = "Album (Deluxe Edition)" };

        var result = SongMapper.MapFromChild(child, client, albumVersion: null);

        Assert.AreEqual("Album (Deluxe Edition)", result.Album);
    }

    [TestMethod]
    public void MapFromChild_WithAlbumVersion_StripsVersionSuffix()
    {
        var client = new FakeSubsonicClient();
        var child = new Child { Id = "s1", Album = "Album (Deluxe Edition)" };

        var result = SongMapper.MapFromChild(child, client, albumVersion: "Deluxe Edition");

        Assert.AreEqual("Album", result.Album);
    }

    [TestMethod]
    public void MapFromChild_NullTitle_ReturnsEmptyString()
    {
        var client = new FakeSubsonicClient();
        var child = new Child { Id = "s1", Title = null };

        var result = SongMapper.MapFromChild(child, client);

        Assert.AreEqual(string.Empty, result.Title);
    }

    [TestMethod]
    public void MapFromChild_NullArtist_ReturnsEmptyString()
    {
        var client = new FakeSubsonicClient();
        var child = new Child { Id = "s1", Artist = null };

        var result = SongMapper.MapFromChild(child, client);

        Assert.AreEqual(string.Empty, result.Artist);
    }

    [TestMethod]
    public void MapFromChild_NullAlbum_ReturnsEmptyString()
    {
        var client = new FakeSubsonicClient();
        var child = new Child { Id = "s1", Album = null };

        var result = SongMapper.MapFromChild(child, client);

        Assert.AreEqual(string.Empty, result.Album);
    }

    [TestMethod]
    public void MapFromChild_NoCoverArt_SetsNullImageFields()
    {
        var client = new FakeSubsonicClient();
        var child = new Child { Id = "s1", CoverArt = null };

        var result = SongMapper.MapFromChild(child, client);

        Assert.IsNull(result.CoverImage);
    }

    [TestMethod]
    public void MapFromChild_EmptyStarred_SetsIsStarredFalse()
    {
        var client = new FakeSubsonicClient();
        var child = new Child { Id = "s1", Starred = null };

        var result = SongMapper.MapFromChild(child, client);

        Assert.IsFalse(result.IsStarred);
    }

    [TestMethod]
    public void MapFromChild_WithStructuredArtists_MapsCorrectly()
    {
        var client = new FakeSubsonicClient();
        var child = new Child
        {
            Id = "s1",
            Artist = "Dr. Dre",
            ArtistId = "a1",
            Artists = [new ArtistID3 { Name = "Dr. Dre", Id = "a1" }, new ArtistID3 { Name = "Eminem", Id = "a2" }],
        };

        var result = SongMapper.MapFromChild(child, client);

        Assert.IsTrue(result.HasStructuredArtists);
        Assert.AreEqual(2, result.Artists.Count);
        Assert.AreEqual("Dr. Dre", result.Artists[0].Name);
        Assert.AreEqual("a1", result.Artists[0].Id);
        Assert.AreEqual("Eminem", result.Artists[1].Name);
        Assert.AreEqual("a2", result.Artists[1].Id);
    }
}
