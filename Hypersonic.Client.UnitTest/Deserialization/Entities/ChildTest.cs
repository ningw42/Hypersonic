using Hypersonic.Client.Models.Entities;
using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Client.UnitTest.Deserialization.Entities;

[TestClass]
public class ChildTest
{
    public const string JSON_WithArtists = """
        {
            "id": "300000116",
            "parent": "200000021",
            "title": "Can I Help U?",
            "isDir": false,
            "isVideo": false,
            "type": "music",
            "albumId": "200000021",
            "album": "Forget and Remember",
            "artistId": "100000036",
            "artist": "Comfort Fit",
            "coverArt": "300000116",
            "duration": 103,
            "bitRate": 216,
            "track": 1,
            "year": 2005,
            "suffix": "mp3",
            "displayArtist": "Comfort Fit feat. Jazzmaster",
            "artists": [
                {
                    "id": "100000036",
                    "name": "Comfort Fit"
                },
                {
                    "id": "100000099",
                    "name": "Jazzmaster"
                }
            ]
        }
        """;

    public const string JSON_WithoutArtists = """
        {
            "id": "300000116",
            "parent": "200000021",
            "title": "Can I Help U?",
            "isDir": false,
            "isVideo": false,
            "type": "music",
            "albumId": "200000021",
            "album": "Forget and Remember",
            "artistId": "100000036",
            "artist": "Comfort Fit",
            "coverArt": "300000116",
            "duration": 103,
            "bitRate": 216,
            "track": 1,
            "year": 2005,
            "suffix": "mp3"
        }
        """;

    public const string XML_WithArtists = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1">
            <album id="200000021" name="Forget and Remember" artist="Comfort Fit" artistId="100000036" songCount="1" duration="103">
                <song id="300000116" parent="200000021" title="Can I Help U?" isDir="false" isVideo="false" type="music"
                      albumId="200000021" album="Forget and Remember" artistId="100000036" artist="Comfort Fit"
                      coverArt="300000116" duration="103" bitRate="216" track="1" year="2005" suffix="mp3"
                      displayArtist="Comfort Fit feat. Jazzmaster">
                    <artists id="100000036" name="Comfort Fit"/>
                    <artists id="100000099" name="Jazzmaster"/>
                </song>
            </album>
        </subsonic-response>
        """;

    public const string XML_WithoutArtists = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1">
            <album id="200000021" name="Forget and Remember" artist="Comfort Fit" artistId="100000036" songCount="1" duration="103">
                <song id="300000116" parent="200000021" title="Can I Help U?" isDir="false" isVideo="false" type="music"
                      albumId="200000021" album="Forget and Remember" artistId="100000036" artist="Comfort Fit"
                      coverArt="300000116" duration="103" bitRate="216" track="1" year="2005" suffix="mp3"/>
            </album>
        </subsonic-response>
        """;

    [TestMethod]
    public void Test_JSON_WithArtists_DeserializesArtistsArray()
    {
        var child = SubsonicClient.DeserializeJson<Child>(JSON_WithArtists.ToStream());

        Assert.IsNotNull(child);
        Assert.IsNotNull(child.Artists);
        Assert.AreEqual(2, child.Artists.Length);
        Assert.AreEqual("100000036", child.Artists[0].Id);
        Assert.AreEqual("Comfort Fit", child.Artists[0].Name);
        Assert.AreEqual("100000099", child.Artists[1].Id);
        Assert.AreEqual("Jazzmaster", child.Artists[1].Name);
    }

    [TestMethod]
    public void Test_JSON_WithArtists_DeserializesDisplayArtist()
    {
        var child = SubsonicClient.DeserializeJson<Child>(JSON_WithArtists.ToStream());

        Assert.IsNotNull(child);
        Assert.AreEqual("Comfort Fit feat. Jazzmaster", child.DisplayArtist);
    }

    [TestMethod]
    public void Test_JSON_WithoutArtists_ArtistsIsNull()
    {
        var child = SubsonicClient.DeserializeJson<Child>(JSON_WithoutArtists.ToStream());

        Assert.IsNotNull(child);
        Assert.IsNull(child.Artists);
        Assert.IsNull(child.DisplayArtist);
        Assert.AreEqual("Comfort Fit", child.Artist);
        Assert.AreEqual("100000036", child.ArtistId);
    }

    [TestMethod]
    public void Test_XML_WithArtists_DeserializesArtistsArray()
    {
        var response = SubsonicClient.DeserializeXmlResponse<GetAlbumResponse>(XML_WithArtists.ToStream());

        Assert.IsNotNull(response);
        var song = response.Album.Song[0];
        Assert.IsNotNull(song.Artists);
        Assert.AreEqual(2, song.Artists.Length);
        Assert.AreEqual("100000036", song.Artists[0].Id);
        Assert.AreEqual("Comfort Fit", song.Artists[0].Name);
        Assert.AreEqual("100000099", song.Artists[1].Id);
        Assert.AreEqual("Jazzmaster", song.Artists[1].Name);
    }

    [TestMethod]
    public void Test_XML_WithArtists_DeserializesDisplayArtist()
    {
        var response = SubsonicClient.DeserializeXmlResponse<GetAlbumResponse>(XML_WithArtists.ToStream());

        Assert.IsNotNull(response);
        var song = response.Album.Song[0];
        Assert.AreEqual("Comfort Fit feat. Jazzmaster", song.DisplayArtist);
    }

    [TestMethod]
    public void Test_XML_WithoutArtists_ArtistsIsNull()
    {
        var response = SubsonicClient.DeserializeXmlResponse<GetAlbumResponse>(XML_WithoutArtists.ToStream());

        Assert.IsNotNull(response);
        var song = response.Album.Song[0];
        Assert.IsNull(song.Artists);
        Assert.IsNull(song.DisplayArtist);
        Assert.AreEqual("Comfort Fit", song.Artist);
        Assert.AreEqual("100000036", song.ArtistId);
    }
}
