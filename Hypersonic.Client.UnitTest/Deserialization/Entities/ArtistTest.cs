using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.UnitTest.Deserialization;

[TestClass]
public class ArtistTest
{
    public const string OpenSubsonic_Example_JSON = """
        {
            "id": "100000002",
            "name": "Synthetic",
            "coverArt": "ar-100000002",
            "albumCount": 1,
            "starred": "2021-02-22T05:54:18Z"
        }
        """;

    public const string Navidrome_DaftPunk_JSON = """
        {
            "id": "39bfe262546e5598284a148b1eb59d4a",
            "name": "Daft Punk",
            "coverArt": "ar-39bfe262546e5598284a148b1eb59d4a_0",
            "albumCount": 3,
            "artistImageUrl": "https://navidrome.ningw.net/share/img/eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6ImFyLTM5YmZlMjYyNTQ2ZTU1OTgyODRhMTQ4YjFlYjU5ZDRhXzAiLCJpc3MiOiJORCJ9.4Ofj0wAlX1XcaAVYV_CYqQHsXY4kuooFS8oHbKUJNU4?size=600",
            "musicBrainzId": "056e4f3e-d505-4dad-8ec1-d04f521cbb56",
            "sortName": "Daft Punk",
            "album": [
                {
                    "id": "7a40c82078251be3ebbe7d11efb8912d",
                    "parent": "39bfe262546e5598284a148b1eb59d4a",
                    "isDir": true,
                    "title": "Homework (25th Anniversary Edition)",
                    "name": "Homework (25th Anniversary Edition)",
                    "album": "Homework (25th Anniversary Edition)",
                    "artist": "Daft Punk",
                    "year": 1997,
                    "genre": "Dance",
                    "coverArt": "al-7a40c82078251be3ebbe7d11efb8912d_67382644",
                    "duration": 10925,
                    "playCount": 2,
                    "created": "2024-10-22T10:40:31.558374422Z",
                    "artistId": "39bfe262546e5598284a148b1eb59d4a",
                    "songCount": 31,
                    "isVideo": false,
                    "played": "2024-12-02T13:46:06.5911086+08:00",
                    "bpm": 0,
                    "comment": "",
                    "sortName": "",
                    "mediaType": "album",
                    "musicBrainzId": "1ca31e9e-67a3-4028-98de-deaed8f13779",
                    "genres": [],
                    "replayGain": {},
                    "channelCount": 0,
                    "samplingRate": 0
                },
                {
                    "id": "31425f4931e3cc157f5783a71693eef6",
                    "parent": "39bfe262546e5598284a148b1eb59d4a",
                    "isDir": true,
                    "title": "Discovery",
                    "name": "Discovery",
                    "album": "Discovery",
                    "artist": "Daft Punk",
                    "year": 2001,
                    "genre": "Dance",
                    "coverArt": "al-31425f4931e3cc157f5783a71693eef6_67382620",
                    "duration": 3669,
                    "playCount": 2,
                    "created": "2024-10-22T13:28:18.70945885Z",
                    "artistId": "39bfe262546e5598284a148b1eb59d4a",
                    "songCount": 14,
                    "isVideo": false,
                    "played": "2024-11-08T15:45:59.130102093+08:00",
                    "bpm": 0,
                    "comment": "",
                    "sortName": "",
                    "mediaType": "album",
                    "musicBrainzId": "6cd30d99-4923-4d5e-8e51-9d87506976f1",
                    "genres": [],
                    "replayGain": {},
                    "channelCount": 0,
                    "samplingRate": 0
                },
                {
                    "id": "fd30249ba11d17fb1958c9642c1d7c85",
                    "parent": "39bfe262546e5598284a148b1eb59d4a",
                    "isDir": true,
                    "title": "Random Access Memories",
                    "name": "Random Access Memories",
                    "album": "Random Access Memories",
                    "artist": "Daft Punk",
                    "year": 2013,
                    "genre": "Electronic",
                    "coverArt": "al-fd30249ba11d17fb1958c9642c1d7c85_67382653",
                    "duration": 4474,
                    "playCount": 21,
                    "created": "2024-10-21T09:54:01.002393104Z",
                    "artistId": "39bfe262546e5598284a148b1eb59d4a",
                    "songCount": 13,
                    "isVideo": false,
                    "played": "2024-12-04T23:19:07.791888773+08:00",
                    "bpm": 0,
                    "comment": "",
                    "sortName": "",
                    "mediaType": "album",
                    "musicBrainzId": "e69e2f55-a2c0-472e-b30b-f43b565b3fbe",
                    "genres": [],
                    "replayGain": {},
                    "channelCount": 0,
                    "samplingRate": 0
                }
            ]
        }
        """;

    public const string Navidrome_DaftPunk_XML = """
        <artist id="39bfe262546e5598284a148b1eb59d4a" name="Daft Punk" coverArt="ar-39bfe262546e5598284a148b1eb59d4a_0" albumCount="3" artistImageUrl="https://navidrome.ningw.net/share/img/eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6ImFyLTM5YmZlMjYyNTQ2ZTU1OTgyODRhMTQ4YjFlYjU5ZDRhXzAiLCJpc3MiOiJORCJ9.4Ofj0wAlX1XcaAVYV_CYqQHsXY4kuooFS8oHbKUJNU4?size=600" musicBrainzId="056e4f3e-d505-4dad-8ec1-d04f521cbb56" sortName="Daft Punk">
            <album id="7a40c82078251be3ebbe7d11efb8912d" parent="39bfe262546e5598284a148b1eb59d4a" isDir="true" title="Homework (25th Anniversary Edition)" name="Homework (25th Anniversary Edition)" album="Homework (25th Anniversary Edition)" artist="Daft Punk" year="1997" genre="Dance" coverArt="al-7a40c82078251be3ebbe7d11efb8912d_67382644" duration="10925" playCount="2" created="2024-10-22T10:40:31.558374422Z" artistId="39bfe262546e5598284a148b1eb59d4a" songCount="31" isVideo="false" played="2024-12-02T13:46:06.5911086+08:00" bpm="0" comment="" sortName="" mediaType="album" musicBrainzId="1ca31e9e-67a3-4028-98de-deaed8f13779" channelCount="0" samplingRate="0">
                <replayGain></replayGain>
            </album>
            <album id="31425f4931e3cc157f5783a71693eef6" parent="39bfe262546e5598284a148b1eb59d4a" isDir="true" title="Discovery" name="Discovery" album="Discovery" artist="Daft Punk" year="2001" genre="Dance" coverArt="al-31425f4931e3cc157f5783a71693eef6_67382620" duration="3669" playCount="2" created="2024-10-22T13:28:18.70945885Z" artistId="39bfe262546e5598284a148b1eb59d4a" songCount="14" isVideo="false" played="2024-11-08T15:45:59.130102093+08:00" bpm="0" comment="" sortName="" mediaType="album" musicBrainzId="6cd30d99-4923-4d5e-8e51-9d87506976f1" channelCount="0" samplingRate="0">
                <replayGain></replayGain>
            </album>
            <album id="fd30249ba11d17fb1958c9642c1d7c85" parent="39bfe262546e5598284a148b1eb59d4a" isDir="true" title="Random Access Memories" name="Random Access Memories" album="Random Access Memories" artist="Daft Punk" year="2013" genre="Electronic" coverArt="al-fd30249ba11d17fb1958c9642c1d7c85_67382653" duration="4474" playCount="21" created="2024-10-21T09:54:01.002393104Z" artistId="39bfe262546e5598284a148b1eb59d4a" songCount="13" isVideo="false" played="2024-12-04T23:19:07.791888773+08:00" bpm="0" comment="" sortName="" mediaType="album" musicBrainzId="e69e2f55-a2c0-472e-b30b-f43b565b3fbe" channelCount="0" samplingRate="0">
                <replayGain></replayGain>
            </album>
        </artist>
        """;

    [TestMethod]
    public void Test_OpenSubsonic_Example_JSON()
    {
        Artist artist = SubsonicClient.DeserializeJson<Artist>(ArtistTest.OpenSubsonic_Example_JSON.ToStream());
        Assert.IsNotNull(artist);
        Assert.AreEqual("100000002", artist.Id);
        Assert.AreEqual("Synthetic", artist.Name);
        Assert.AreEqual("ar-100000002", artist.CoverArt);
        Assert.AreEqual(1, artist.AlbumCount);
        Assert.AreEqual("2021-02-22T05:54:18Z", artist.Starred);
    }

    [TestMethod]
    public void Test_Navidrome_DaftPunk_JSON()
    {
        Artist artist = SubsonicClient.DeserializeJson<Artist>(ArtistTest.Navidrome_DaftPunk_JSON.ToStream());
        ArtistTest.Navidrome_DaftPunk_Assertion(artist);
    }

    [TestMethod]
    public void Test_Navidrome_DaftPunk_XML()
    {
        Artist artist = SubsonicClient.DeserializeXml<Artist>(ArtistTest.Navidrome_DaftPunk_XML.ToStream());
        ArtistTest.Navidrome_DaftPunk_Assertion(artist);
    }

    private static void Navidrome_DaftPunk_Assertion(Artist artist)
    {
        Assert.IsNotNull(artist);
        Assert.AreEqual("39bfe262546e5598284a148b1eb59d4a", artist.Id);
        Assert.AreEqual("Daft Punk", artist.Name);
        Assert.AreEqual("ar-39bfe262546e5598284a148b1eb59d4a_0", artist.CoverArt);
        Assert.AreEqual(3, artist.AlbumCount);
        Assert.AreEqual("https://navidrome.ningw.net/share/img/eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6ImFyLTM5YmZlMjYyNTQ2ZTU1OTgyODRhMTQ4YjFlYjU5ZDRhXzAiLCJpc3MiOiJORCJ9.4Ofj0wAlX1XcaAVYV_CYqQHsXY4kuooFS8oHbKUJNU4?size=600", artist.ArtistImageUrl);
        Assert.AreEqual("056e4f3e-d505-4dad-8ec1-d04f521cbb56", artist.MusicBrainzId);
        Assert.AreEqual("Daft Punk", artist.SortName);

        Assert.AreEqual(3, artist.Album.Length);
        Assert.AreEqual("7a40c82078251be3ebbe7d11efb8912d", artist.Album[0].Id);
        Assert.AreEqual("Homework (25th Anniversary Edition)", artist.Album[0].Name);
        Assert.AreEqual(1997, artist.Album[0].Year);
        Assert.AreEqual(31, artist.Album[0].SongCount);

        Assert.AreEqual("31425f4931e3cc157f5783a71693eef6", artist.Album[1].Id);
        Assert.AreEqual("Discovery", artist.Album[1].Name);
        Assert.AreEqual(2001, artist.Album[1].Year);
        Assert.AreEqual(14, artist.Album[1].SongCount);

        Assert.AreEqual("fd30249ba11d17fb1958c9642c1d7c85", artist.Album[2].Id);
        Assert.AreEqual("Random Access Memories", artist.Album[2].Name);
        Assert.AreEqual(2013, artist.Album[2].Year);
        Assert.AreEqual(13, artist.Album[2].SongCount);
    }
}
