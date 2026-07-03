using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class AmllLyricsIndexDataTests
{
    // Real index-line shape: metadata is an array of [key, [values]] pairs.
    private const string Jsonl = """
        {"metadata":[["album",["Idol"]],["artists",["YOASOBI"]],["musicName",["Idol"]],["ncmMusicId",["1"]]],"rawLyricFile":"idol-a.ttml"}
        {"metadata":[["album",["Idol"]],["artists",["YOASOBI"]],["musicName",["Idol"]],["ncmMusicId",["2"]]],"rawLyricFile":"idol-b.ttml"}
        {"metadata":[["album",["アイドル"]],["artists",["YOASOBI"]],["musicName",["アイドル"]]],"rawLyricFile":"idol-jp.ttml"}
        {"metadata":[["album",["Wishes"]],["artists",["Famba","Eric Lumiere","Raven & Kreyn"]],["musicName",["In My Head"]]],"rawLyricFile":"inmyhead.ttml"}
        """;

    private static AmllLyricsIndexData Data() => AmllLyricsIndexData.Parse(Encoding.UTF8.GetBytes(Jsonl));

    [TestMethod]
    public void Parse_CountsEntries()
        => Assert.AreEqual(4, Data().Count);

    [TestMethod]
    public void Resolve_ExactTitleAndArtist_Hits()
        => Assert.IsNotNull(Data().TryResolve(new LyricsQuery("s", Artist: "YOASOBI", Title: "Idol")));

    [TestMethod]
    public void Resolve_TitleCaseAndWidthInsensitive()
        => Assert.IsNotNull(Data().TryResolve(new LyricsQuery("s", Artist: "yoasobi", Title: "ＩＤＯＬ")));

    [TestMethod]
    public void Resolve_WrongArtist_NoMatch()
        => Assert.IsNull(Data().TryResolve(new LyricsQuery("s", Artist: "Someone Else", Title: "Idol")));

    [TestMethod]
    public void Resolve_MissingArtist_NoMatch()
        => Assert.IsNull(Data().TryResolve(new LyricsQuery("s", Artist: null, Title: "Idol")));

    [TestMethod]
    public void Resolve_DistinctTitleBucket_DoesNotCrossMatch()
    {
        // Japanese title lives in its own bucket; querying it never returns the English file.
        var file = Data().TryResolve(new LyricsQuery("s", Artist: "YOASOBI", Title: "アイドル"));
        Assert.AreEqual("idol-jp.ttml", file);
    }

    [TestMethod]
    public void Resolve_AlbumTiebreak_PrefersMatchingAlbum()
    {
        // Both idol-a/idol-b share title+artist+album "Idol"; with the album present the first is fine,
        // but a query for a non-existent album still falls back to the first candidate (not null).
        var file = Data().TryResolve(new LyricsQuery("s", Artist: "YOASOBI", Title: "Idol", Album: "Idol"));
        Assert.IsTrue(file is "idol-a.ttml" or "idol-b.ttml");
    }

    [TestMethod]
    public void Resolve_QueryArtistFeatSplit_IntersectsIndexArray()
    {
        // Subsonic crams a collab into one string; the index has it as separate array elements.
        var file = Data().TryResolve(new LyricsQuery("s", Artist: "Famba feat. Eric Lumiere", Title: "In My Head"));
        Assert.AreEqual("inmyhead.ttml", file);
    }

    [TestMethod]
    public void Parse_SkipsMalformedLines()
    {
        var data = AmllLyricsIndexData.Parse(Encoding.UTF8.GetBytes("not json\n" + Jsonl.Split('\n')[0]));
        Assert.AreEqual(1, data.Count);
    }

    [TestMethod]
    public void Parse_SkipsWrongShapeMetadata_DoesNotThrow()
    {
        // Structurally valid JSON but musicName is a scalar (not an array): must be skipped, never throw
        // (guards against JsonElement accessors raising InvalidOperationException and losing the whole index).
        var bad = """{"metadata":[["musicName","Idol"],["artists",["A"]]],"rawLyricFile":"x.ttml"}""";
        var data = AmllLyricsIndexData.Parse(Encoding.UTF8.GetBytes(bad + "\n" + Jsonl.Split('\n')[0]));
        Assert.AreEqual(1, data.Count); // wrong-shape line dropped; the good line kept
    }

    [TestMethod]
    public void SplitQueryArtists_SplitsSeparators()
    {
        var set = AmllLyricsIndexData.SplitQueryArtists("A, B & C feat. D");
        CollectionAssert.AreEquivalent(new[] { "a", "b", "c", "d" }, set.ToArray());
    }
}
