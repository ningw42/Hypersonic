using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;

namespace Hypersonic.Core.Tests.Helpers;

[TestClass]
public class SongArtistMapperTests
{
    [TestMethod]
    public void MapArtists_WithPopulatedArtists_ReturnsMappedListAndHasStructuredTrue()
    {
        var input = new[] { ("Alice", (string?)"1"), ("Bob", (string?)"2") };

        var (artists, hasStructured) = SongArtistMapper.MapArtists(input, "Fallback", "99");

        Assert.IsTrue(hasStructured);
        Assert.AreEqual(2, artists.Count);
        Assert.AreEqual("Alice", artists[0].Name);
        Assert.AreEqual("1", artists[0].Id);
        Assert.AreEqual("Bob", artists[1].Name);
        Assert.AreEqual("2", artists[1].Id);
    }

    [TestMethod]
    public void MapArtists_WithNullArtists_FallsBackToSingleEntry()
    {
        var (artists, hasStructured) = SongArtistMapper.MapArtists(null, "Fallback", "99");

        Assert.IsFalse(hasStructured);
        Assert.AreEqual(1, artists.Count);
        Assert.AreEqual("Fallback", artists[0].Name);
        Assert.AreEqual("99", artists[0].Id);
    }

    [TestMethod]
    public void MapArtists_WithEmptyArray_FallsBackToSingleEntry()
    {
        var input = Array.Empty<(string, string?)>();

        var (artists, hasStructured) = SongArtistMapper.MapArtists(input, "Fallback", "99");

        Assert.IsFalse(hasStructured);
        Assert.AreEqual(1, artists.Count);
        Assert.AreEqual("Fallback", artists[0].Name);
    }

    [TestMethod]
    public void MapArtists_WithNullFallback_ProducesEmptyName()
    {
        var (artists, hasStructured) = SongArtistMapper.MapArtists(null, null, null);

        Assert.IsFalse(hasStructured);
        Assert.AreEqual(1, artists.Count);
        Assert.AreEqual(string.Empty, artists[0].Name);
        Assert.IsNull(artists[0].Id);
    }

    [TestMethod]
    public void MapArtists_WithAllEmptyNames_FallsBack()
    {
        var input = new[] { ("", (string?)"1"), ("", (string?)"2") };

        var (artists, hasStructured) = SongArtistMapper.MapArtists(input, "Fallback", "99");

        Assert.IsFalse(hasStructured);
        Assert.AreEqual(1, artists.Count);
        Assert.AreEqual("Fallback", artists[0].Name);
    }
}
