using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Client.UnitTest.Deserialization;

[TestClass]
public class GetAlbumList2ResponseTest
{
    public const string Navidrome_JSON_Response = """
        {
            "subsonic-response": {
                "status": "ok",
                "version": "1.16.1",
                "type": "navidrome",
                "serverVersion": "0.53.3 (v0.53.3)",
                "openSubsonic": true,
                "albumList2": {
                    "album": [
                        {
                            "id": "al-1",
                            "name": "Random Access Memories",
                            "artist": "Daft Punk",
                            "artistId": "ar-1",
                            "coverArt": "al-1",
                            "songCount": 13,
                            "duration": 4477,
                            "playCount": 42,
                            "created": "2024-01-15T10:30:00Z",
                            "year": 2013,
                            "genre": "Electronic"
                        },
                        {
                            "id": "al-2",
                            "name": "OK Computer",
                            "artist": "Radiohead",
                            "artistId": "ar-2",
                            "coverArt": "al-2",
                            "songCount": 12,
                            "duration": 3214,
                            "playCount": 100,
                            "created": "2024-01-10T08:00:00Z",
                            "year": 1997,
                            "genre": "Alternative Rock"
                        }
                    ]
                }
            }
        }
        """;

    public const string Navidrome_XML_Response = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1" type="navidrome" serverVersion="0.53.3 (v0.53.3)" openSubsonic="true">
            <albumList2>
                <album id="al-1" name="Random Access Memories" artist="Daft Punk" artistId="ar-1" coverArt="al-1" songCount="13" duration="4477" playCount="42" created="2024-01-15T10:30:00Z" year="2013" genre="Electronic"/>
                <album id="al-2" name="OK Computer" artist="Radiohead" artistId="ar-2" coverArt="al-2" songCount="12" duration="3214" playCount="100" created="2024-01-10T08:00:00Z" year="1997" genre="Alternative Rock"/>
            </albumList2>
        </subsonic-response>
        """;

    public const string JSON_Empty_AlbumList = """
        {
            "subsonic-response": {
                "status": "ok",
                "version": "1.16.1",
                "type": "navidrome",
                "serverVersion": "0.53.3 (v0.53.3)",
                "openSubsonic": true,
                "albumList2": {}
            }
        }
        """;

    [TestMethod]
    public void Test_Navidrome_JSON_AlbumList2()
    {
        var response = SubsonicClient.DeserializeJsonResponse<GetAlbumList2Response>(
            Navidrome_JSON_Response.ToStream());

        Assert.IsNotNull(response);
        Assert.AreEqual("ok", response.Status);
        Assert.IsNotNull(response.AlbumList2);
        Assert.IsNotNull(response.AlbumList2.Album);
        Assert.AreEqual(2, response.AlbumList2.Album.Length);

        var album1 = response.AlbumList2.Album[0];
        Assert.AreEqual("al-1", album1.Id);
        Assert.AreEqual("Random Access Memories", album1.Name);
        Assert.AreEqual("Daft Punk", album1.Artist);
        Assert.AreEqual("al-1", album1.CoverArt);
        Assert.AreEqual(13, album1.SongCount);
        Assert.AreEqual(4477, album1.Duration);
        Assert.AreEqual(2013, album1.Year);
        Assert.AreEqual("Electronic", album1.Genre);

        var album2 = response.AlbumList2.Album[1];
        Assert.AreEqual("al-2", album2.Id);
        Assert.AreEqual("OK Computer", album2.Name);
        Assert.AreEqual("Radiohead", album2.Artist);
        Assert.AreEqual(1997, album2.Year);
    }

    [TestMethod]
    public void Test_Navidrome_XML_AlbumList2()
    {
        var response = SubsonicClient.DeserializeXmlResponse<GetAlbumList2Response>(
            Navidrome_XML_Response.ToStream());

        Assert.IsNotNull(response);
        Assert.AreEqual("ok", response.Status);
        Assert.IsNotNull(response.AlbumList2);
        Assert.IsNotNull(response.AlbumList2.Album);
        Assert.AreEqual(2, response.AlbumList2.Album.Length);

        var album1 = response.AlbumList2.Album[0];
        Assert.AreEqual("al-1", album1.Id);
        Assert.AreEqual("Random Access Memories", album1.Name);
        Assert.AreEqual("Daft Punk", album1.Artist);
        Assert.AreEqual(13, album1.SongCount);
        Assert.AreEqual(2013, album1.Year);

        var album2 = response.AlbumList2.Album[1];
        Assert.AreEqual("al-2", album2.Id);
        Assert.AreEqual("OK Computer", album2.Name);
        Assert.AreEqual(1997, album2.Year);
    }

    [TestMethod]
    public void Test_JSON_Empty_AlbumList2()
    {
        var response = SubsonicClient.DeserializeJsonResponse<GetAlbumList2Response>(
            JSON_Empty_AlbumList.ToStream());

        Assert.IsNotNull(response);
        Assert.AreEqual("ok", response.Status);
        Assert.IsNotNull(response.AlbumList2);
        Assert.IsNull(response.AlbumList2.Album);
    }
}
