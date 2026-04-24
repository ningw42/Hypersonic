using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.UnitTest.Deserialization;

[TestClass]
public class AlbumID3VersionTest
{
    public const string AlbumWithVersion_JSON = """
        {
            "id": "ad0f112b6dcf83de5e9cae85d07f0d35",
            "name": "Abbey Road (Super Deluxe Edition)",
            "version": "Super Deluxe Edition",
            "artist": "The Beatles",
            "artistId": "artist-1",
            "year": 2019,
            "songCount": 40,
            "duration": 6240
        }
        """;

    public const string AlbumWithCleanNameAndVersion_JSON = """
        {
            "id": "ad0f112b6dcf83de5e9cae85d07f0d35",
            "name": "Abbey Road",
            "version": "Super Deluxe Edition",
            "artist": "The Beatles",
            "artistId": "artist-1",
            "year": 2019,
            "songCount": 40,
            "duration": 6240
        }
        """;

    public const string AlbumWithoutVersion_JSON = """
        {
            "id": "ad0f112b6dcf83de5e9cae85d07f0d35",
            "name": "Abbey Road",
            "artist": "The Beatles",
            "artistId": "artist-1",
            "year": 1969,
            "songCount": 17,
            "duration": 2822
        }
        """;

    public const string AlbumWithEmptyVersion_JSON = """
        {
            "id": "ad0f112b6dcf83de5e9cae85d07f0d35",
            "name": "Abbey Road",
            "version": "",
            "artist": "The Beatles",
            "artistId": "artist-1",
            "year": 1969,
            "songCount": 17,
            "duration": 2822
        }
        """;

    [TestMethod]
    public void Deserialize_WithVersion_PopulatesVersionField()
    {
        var album = SubsonicClient.DeserializeJson<AlbumID3>(AlbumWithVersion_JSON.ToStream());

        Assert.IsNotNull(album);
        Assert.AreEqual("Abbey Road (Super Deluxe Edition)", album.Name);
        Assert.AreEqual("Super Deluxe Edition", album.Version);
        Assert.AreEqual("The Beatles", album.Artist);
        Assert.AreEqual(2019, album.Year);
    }

    [TestMethod]
    public void Deserialize_WithCleanNameAndVersion_PopulatesBothFields()
    {
        var album = SubsonicClient.DeserializeJson<AlbumID3>(AlbumWithCleanNameAndVersion_JSON.ToStream());

        Assert.IsNotNull(album);
        Assert.AreEqual("Abbey Road", album.Name);
        Assert.AreEqual("Super Deluxe Edition", album.Version);
    }

    [TestMethod]
    public void Deserialize_WithoutVersion_VersionIsNull()
    {
        var album = SubsonicClient.DeserializeJson<AlbumID3>(AlbumWithoutVersion_JSON.ToStream());

        Assert.IsNotNull(album);
        Assert.AreEqual("Abbey Road", album.Name);
        Assert.IsNull(album.Version);
    }

    [TestMethod]
    public void Deserialize_WithEmptyVersion_VersionIsEmptyString()
    {
        var album = SubsonicClient.DeserializeJson<AlbumID3>(AlbumWithEmptyVersion_JSON.ToStream());

        Assert.IsNotNull(album);
        Assert.AreEqual("Abbey Road", album.Name);
        Assert.AreEqual("", album.Version);
    }

    [TestMethod]
    public void Deserialize_WithVersion_OtherFieldsIntact()
    {
        var album = SubsonicClient.DeserializeJson<AlbumID3>(AlbumWithVersion_JSON.ToStream());

        Assert.IsNotNull(album);
        Assert.AreEqual("ad0f112b6dcf83de5e9cae85d07f0d35", album.Id);
        Assert.AreEqual("artist-1", album.ArtistId);
        Assert.AreEqual(40, album.SongCount);
        Assert.AreEqual(6240, album.Duration);
    }
}
