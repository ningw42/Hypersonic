using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Client.UnitTest.Deserialization;

[TestClass]
public class GetLyricsBySongIdResponseTest
{
    public const string JSON_Synced_Lyrics = """
        {
            "subsonic-response": {
                "status": "ok",
                "version": "1.16.1",
                "type": "navidrome",
                "serverVersion": "0.53.3 (v0.53.3)",
                "openSubsonic": true,
                "lyricsList": {
                    "structuredLyrics": [
                        {
                            "displayArtist": "Artist",
                            "displayTitle": "Song",
                            "lang": "eng",
                            "offset": 0,
                            "synced": true,
                            "line": [
                                { "start": 0, "value": "First line" },
                                { "start": 2500, "value": "Second line" }
                            ]
                        }
                    ]
                }
            }
        }
        """;

    public const string XML_Unsynced_Lyrics = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1" type="navidrome" serverVersion="0.53.3 (v0.53.3)" openSubsonic="true">
            <lyricsList>
                <structuredLyrics displayArtist="Artist" displayTitle="Song" lang="eng" synced="false">
                    <line>First line</line>
                    <line>Second line</line>
                </structuredLyrics>
            </lyricsList>
        </subsonic-response>
        """;

    [TestMethod]
    public void Test_JSON_Synced_Lyrics_PreservesStartAndOffset()
    {
        // Round-trip guard: ensure [JsonPropertyName("start")] and ("offset")
        // are still wired up after the [XmlAttribute] → [XmlIgnore] migration.
        // If this regresses, synced-lyrics work (roadmap 4.5) silently loses
        // its timing data on JSON servers.
        var response = SubsonicClient.DeserializeJsonResponse<GetLyricsBySongIdResponse>(
            JSON_Synced_Lyrics.ToStream());

        Assert.IsNotNull(response);
        Assert.AreEqual("ok", response.Status);
        Assert.IsNotNull(response.LyricsList);
        Assert.IsNotNull(response.LyricsList.StructuredLyrics);
        Assert.AreEqual(1, response.LyricsList.StructuredLyrics.Length);

        var entry = response.LyricsList.StructuredLyrics[0];
        Assert.AreEqual("Artist", entry.DisplayArtist);
        Assert.AreEqual("Song", entry.DisplayTitle);
        Assert.AreEqual(0L, entry.Offset);
        Assert.IsTrue(entry.Synced);

        Assert.IsNotNull(entry.Line);
        Assert.AreEqual(2, entry.Line.Length);
        Assert.AreEqual(0L, entry.Line[0].Start);
        Assert.AreEqual("First line", entry.Line[0].Value);
        Assert.AreEqual(2500L, entry.Line[1].Start);
        Assert.AreEqual("Second line", entry.Line[1].Value);
    }

    [TestMethod]
    public void Test_XML_Unsynced_Lyrics_DeserializesWithoutCrash()
    {
        // The XML reflection-importer used to crash on Nullable<long> +
        // [XmlAttribute]. After the [XmlIgnore] migration, XML lyrics
        // responses must deserialize cleanly. start/offset are not exposed
        // via XML in v1; that's a synced-lyrics (4.5) follow-up.
        var response = SubsonicClient.DeserializeXmlResponse<GetLyricsBySongIdResponse>(
            XML_Unsynced_Lyrics.ToStream());

        Assert.IsNotNull(response);
        Assert.AreEqual("ok", response.Status);
        Assert.IsNotNull(response.LyricsList);
        Assert.IsNotNull(response.LyricsList.StructuredLyrics);
        Assert.AreEqual(1, response.LyricsList.StructuredLyrics.Length);

        var entry = response.LyricsList.StructuredLyrics[0];
        Assert.IsNull(entry.Offset, "Offset is [XmlIgnore] in v1 — XML omits it");
        Assert.IsFalse(entry.Synced);
        Assert.IsNotNull(entry.Line);
        Assert.AreEqual(2, entry.Line.Length);
        Assert.AreEqual("First line", entry.Line[0].Value);
        Assert.IsNull(entry.Line[0].Start, "Start is [XmlIgnore] in v1 — XML omits it");
        Assert.AreEqual("Second line", entry.Line[1].Value);
    }
}
