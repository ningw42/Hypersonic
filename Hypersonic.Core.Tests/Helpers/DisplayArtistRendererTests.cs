using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;

namespace Hypersonic.Core.Tests.Helpers;

[TestClass]
public class DisplayArtistRendererTests
{
    [TestMethod]
    public void BuildSegments_MultiArtistWithDisplayArtist_ProducesAlternatingSegments()
    {
        var artists = new List<SongArtistInfo>
        {
            new() { Name = "Dr. Dre", Id = "1" },
            new() { Name = "Eminem", Id = "2" },
            new() { Name = "Skylar Grey", Id = "3" },
        };

        var result = DisplayArtistRenderer.BuildSegments(
            "Dr. Dre feat. Eminem & Skylar Grey", artists, true);

        Assert.IsFalse(result.UsedStructuredFallback);
        Assert.IsFalse(result.HasUnmatchedArtists);
        Assert.AreEqual(5, result.Segments.Count);
        Assert.AreEqual(ArtistSegmentKind.Artist, result.Segments[0].Kind);
        Assert.AreEqual("Dr. Dre", result.Segments[0].Text);
        Assert.AreEqual(ArtistSegmentKind.Connector, result.Segments[1].Kind);
        Assert.AreEqual(" feat. ", result.Segments[1].Text);
        Assert.AreEqual(ArtistSegmentKind.Artist, result.Segments[2].Kind);
        Assert.AreEqual("Eminem", result.Segments[2].Text);
        Assert.AreEqual(ArtistSegmentKind.Connector, result.Segments[3].Kind);
        Assert.AreEqual(" & ", result.Segments[3].Text);
        Assert.AreEqual(ArtistSegmentKind.Artist, result.Segments[4].Kind);
        Assert.AreEqual("Skylar Grey", result.Segments[4].Text);
    }

    [TestMethod]
    public void BuildSegments_SingleArtistMatchesDisplayArtist_ProducesOneArtistSegment()
    {
        var artists = new List<SongArtistInfo> { new() { Name = "Eminem", Id = "1" } };

        var result = DisplayArtistRenderer.BuildSegments("Eminem", artists, true);

        Assert.AreEqual(1, result.Segments.Count);
        Assert.AreEqual(ArtistSegmentKind.Artist, result.Segments[0].Kind);
        Assert.AreEqual("1", result.Segments[0].Artist!.Id);
    }

    [TestMethod]
    public void BuildSegments_SingleArtistInLongerDisplayArtist_ProducesConnectorAndArtist()
    {
        var artists = new List<SongArtistInfo> { new() { Name = "Artist A", Id = "1" } };

        var result = DisplayArtistRenderer.BuildSegments(
            "Artist A feat. Artist B", artists, true);

        Assert.AreEqual(2, result.Segments.Count);
        Assert.AreEqual(ArtistSegmentKind.Artist, result.Segments[0].Kind);
        Assert.AreEqual("Artist A", result.Segments[0].Text);
        Assert.AreEqual(ArtistSegmentKind.Connector, result.Segments[1].Kind);
        Assert.AreEqual(" feat. Artist B", result.Segments[1].Text);
    }

    [TestMethod]
    public void BuildSegments_StructuredNoMatch_FallsBackToCommaSeparated()
    {
        var artists = new List<SongArtistInfo>
        {
            new() { Name = "Alice", Id = "1" },
            new() { Name = "Bob", Id = "2" },
        };

        var result = DisplayArtistRenderer.BuildSegments("Various Artists", artists, true);

        Assert.IsTrue(result.UsedStructuredFallback);
        Assert.AreEqual(3, result.Segments.Count);
        Assert.AreEqual("Alice", result.Segments[0].Text);
        Assert.AreEqual(", ", result.Segments[1].Text);
        Assert.AreEqual("Bob", result.Segments[2].Text);
    }

    [TestMethod]
    public void BuildSegments_LegacyFallbackNameNotFound_ProducesPlainText()
    {
        var artists = new List<SongArtistInfo> { new() { Name = "Andre Young", Id = "1" } };

        var result = DisplayArtistRenderer.BuildSegments(
            "Dr. Dre feat. Eminem", artists, false);

        Assert.IsFalse(result.UsedStructuredFallback);
        Assert.AreEqual(1, result.Segments.Count);
        Assert.AreEqual("Dr. Dre feat. Eminem", result.Segments[0].Text);
        Assert.IsNull(result.Segments[0].Artist!.Id);
    }

    [TestMethod]
    public void BuildSegments_EmptyArtistsList_WithDisplayArtist_ProducesSingleSegment()
    {
        var result = DisplayArtistRenderer.BuildSegments(
            "Some Artist", new List<SongArtistInfo>(), false);

        Assert.AreEqual(1, result.Segments.Count);
        Assert.AreEqual("Some Artist", result.Segments[0].Text);
    }

    [TestMethod]
    public void BuildSegments_BothEmpty_ReturnsEmptySegments()
    {
        var result = DisplayArtistRenderer.BuildSegments(
            null, new List<SongArtistInfo>(), false);

        Assert.AreEqual(0, result.Segments.Count);
    }

    [TestMethod]
    public void BuildSegments_SubstringCollision_LongestMatchWins()
    {
        var artists = new List<SongArtistInfo>
        {
            new() { Name = "Art", Id = "1" },
            new() { Name = "Artist", Id = "2" },
        };

        var result = DisplayArtistRenderer.BuildSegments(
            "Artist feat. Art", artists, true);

        Assert.AreEqual(3, result.Segments.Count);
        Assert.AreEqual("Artist", result.Segments[0].Text);
        Assert.AreEqual("2", result.Segments[0].Artist!.Id);
        Assert.AreEqual(" feat. ", result.Segments[1].Text);
        Assert.AreEqual("Art", result.Segments[2].Text);
        Assert.AreEqual("1", result.Segments[2].Artist!.Id);
    }

    [TestMethod]
    public void BuildSegments_BoundaryAwareMatching_DoesNotMatchInsideWord()
    {
        var artists = new List<SongArtistInfo> { new() { Name = "Art", Id = "1" } };

        var result = DisplayArtistRenderer.BuildSegments(
            "Arthur feat. Art", artists, true);

        Assert.AreEqual(2, result.Segments.Count);
        Assert.AreEqual(ArtistSegmentKind.Connector, result.Segments[0].Kind);
        Assert.AreEqual("Arthur feat. ", result.Segments[0].Text);
        Assert.AreEqual(ArtistSegmentKind.Artist, result.Segments[1].Kind);
        Assert.AreEqual("Art", result.Segments[1].Text);
    }

    [TestMethod]
    public void BuildSegments_PartialMatch_HasUnmatchedArtistsTrue()
    {
        var artists = new List<SongArtistInfo>
        {
            new() { Name = "Dr. Dre", Id = "1" },
            new() { Name = "Eminem", Id = "2" },
        };

        var result = DisplayArtistRenderer.BuildSegments(
            "Dr. Dre feat. Slim Shady", artists, true);

        Assert.IsFalse(result.UsedStructuredFallback);
        Assert.IsTrue(result.HasUnmatchedArtists);
        Assert.AreEqual(2, result.Segments.Count);
        Assert.AreEqual("Dr. Dre", result.Segments[0].Text);
        Assert.AreEqual("1", result.Segments[0].Artist!.Id);
        Assert.AreEqual(" feat. Slim Shady", result.Segments[1].Text);
    }

    [TestMethod]
    public void BuildSegments_AllEmptyNamesWithDisplayArtist_ProducesPlainText()
    {
        var artists = new List<SongArtistInfo> { new() { Name = "", Id = "1" } };

        var result = DisplayArtistRenderer.BuildSegments("Some Artist", artists, true);

        Assert.AreEqual(1, result.Segments.Count);
        Assert.AreEqual("Some Artist", result.Segments[0].Text);
        Assert.IsNull(result.Segments[0].Artist!.Id);
    }

    [TestMethod]
    public void BuildSegments_CaseInsensitiveMatching()
    {
        var artists = new List<SongArtistInfo> { new() { Name = "eminem", Id = "1" } };

        var result = DisplayArtistRenderer.BuildSegments("EMINEM", artists, true);

        Assert.AreEqual(1, result.Segments.Count);
        Assert.AreEqual(ArtistSegmentKind.Artist, result.Segments[0].Kind);
        Assert.AreEqual("EMINEM", result.Segments[0].Text);
        Assert.AreEqual("1", result.Segments[0].Artist!.Id);
    }

    [TestMethod]
    public void BuildSegments_MapperShapedEmptyName_NoDisplayArtist_ReturnsEmptySegments()
    {
        var artists = new List<SongArtistInfo> { new() { Name = "", Id = null } };

        var result = DisplayArtistRenderer.BuildSegments(null, artists, false);

        Assert.AreEqual(0, result.Segments.Count);
        Assert.IsFalse(result.UsedStructuredFallback);
        Assert.IsFalse(result.HasUnmatchedArtists);
    }

    [TestMethod]
    public void BuildSegments_MapperShapedEmptyName_WithDisplayArtist_ReturnsPlainText()
    {
        var artists = new List<SongArtistInfo> { new() { Name = "", Id = null } };

        var result = DisplayArtistRenderer.BuildSegments("Some Artist", artists, false);

        Assert.AreEqual(1, result.Segments.Count);
        Assert.AreEqual("Some Artist", result.Segments[0].Text);
        Assert.IsNull(result.Segments[0].Artist!.Id);
    }

    [TestMethod]
    public void BuildFlatText_NormalMatch_ReturnsOriginalDisplayArtist()
    {
        var artists = new List<SongArtistInfo>
        {
            new() { Name = "Dr. Dre", Id = "1" },
            new() { Name = "Eminem", Id = "2" },
        };

        var flat = DisplayArtistRenderer.BuildFlatText("Dr. Dre feat. Eminem", artists, true);

        Assert.AreEqual("Dr. Dre feat. Eminem", flat);
    }

    [TestMethod]
    public void BuildFlatText_CommaFallback_ReturnsCommaSeparatedNames()
    {
        var artists = new List<SongArtistInfo>
        {
            new() { Name = "Artist A", Id = "1" },
            new() { Name = "Artist B", Id = "2" },
        };

        var flat = DisplayArtistRenderer.BuildFlatText("Various Artists", artists, true);

        Assert.AreEqual("Artist A, Artist B", flat);
    }

    [TestMethod]
    public void BuildFlatText_EmptyArtists_ReturnsEmpty()
    {
        var flat = DisplayArtistRenderer.BuildFlatText(null, [], false);

        Assert.AreEqual(string.Empty, flat);
    }
}
