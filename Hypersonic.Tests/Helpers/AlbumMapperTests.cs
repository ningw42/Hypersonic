using Hypersonic.Helpers;

namespace Hypersonic.Tests.Helpers;

[TestClass]
public class AlbumMapperTests
{
    // --- SplitAlbumVersion ---

    [TestMethod]
    public void SplitAlbumVersion_NullName_ReturnsEmptyNameAndNullVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(null, "Deluxe Edition");

        Assert.AreEqual(string.Empty, name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_EmptyName_ReturnsEmptyNameAndNullVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion("", "Deluxe Edition");

        Assert.AreEqual(string.Empty, name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_NullVersion_ReturnsOriginalNameAndNullVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion("Abbey Road", null);

        Assert.AreEqual("Abbey Road", name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_EmptyVersion_ReturnsOriginalNameAndNullVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion("Abbey Road", "");

        Assert.AreEqual("Abbey Road", name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_BothNull_ReturnsEmptyNameAndNullVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(null, null);

        Assert.AreEqual(string.Empty, name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_BothEmpty_ReturnsEmptyNameAndNullVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion("", "");

        Assert.AreEqual(string.Empty, name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_SuffixEmbedded_StripsAndReturnsVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Abbey Road (Super Deluxe Edition)", "Super Deluxe Edition");

        Assert.AreEqual("Abbey Road", name);
        Assert.AreEqual("Super Deluxe Edition", version);
    }

    [TestMethod]
    public void SplitAlbumVersion_SuffixEmbedded_Remastered_StripsAndReturnsVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "OK Computer (Remastered)", "Remastered");

        Assert.AreEqual("OK Computer", name);
        Assert.AreEqual("Remastered", version);
    }

    [TestMethod]
    public void SplitAlbumVersion_SuffixEmbedded_SingleWord_StripsAndReturnsVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Thriller (Deluxe)", "Deluxe");

        Assert.AreEqual("Thriller", name);
        Assert.AreEqual("Deluxe", version);
    }

    [TestMethod]
    public void SplitAlbumVersion_AlreadyClean_PreservesBoth()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Abbey Road", "Super Deluxe Edition");

        Assert.AreEqual("Abbey Road", name);
        Assert.AreEqual("Super Deluxe Edition", version);
    }

    [TestMethod]
    public void SplitAlbumVersion_AlreadyClean_NoOverlap_PreservesBoth()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "8-bit lagerfeuer", "Deluxe Edition");

        Assert.AreEqual("8-bit lagerfeuer", name);
        Assert.AreEqual("Deluxe Edition", version);
    }

    [TestMethod]
    public void SplitAlbumVersion_NonStandardBrackets_DiscardsVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Abbey Road [Super Deluxe Edition]", "Super Deluxe Edition");

        Assert.AreEqual("Abbey Road [Super Deluxe Edition]", name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_NonStandardInlineText_DiscardsVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Abbey Road - Super Deluxe Edition", "Super Deluxe Edition");

        Assert.AreEqual("Abbey Road - Super Deluxe Edition", name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_VersionInMiddle_DiscardsVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "The Deluxe Edition Album", "Deluxe Edition");

        Assert.AreEqual("The Deluxe Edition Album", name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_CaseMismatch_ContainsCheckIsCaseInsensitive()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Album Name with deluxe edition somewhere", "Deluxe Edition");

        Assert.AreEqual("Album Name with deluxe edition somewhere", name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_SuffixMatch_IsCaseSensitive()
    {
        // " (deluxe edition)" != " (Deluxe Edition)" — suffix match is Ordinal
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Album (deluxe edition)", "Deluxe Edition");

        // Contains check finds it (case-insensitive), so it's non-standard → discard
        Assert.AreEqual("Album (deluxe edition)", name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_ParenthesizedButNotSuffix_DiscardsVersion()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Album (Deluxe Edition) Bonus Tracks", "Deluxe Edition");

        Assert.AreEqual("Album (Deluxe Edition) Bonus Tracks", name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_NameContainsParenthesesNotRelatedToVersion_PreservesBoth()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Music (Original Motion Picture Soundtrack)", "Deluxe Edition");

        Assert.AreEqual("Music (Original Motion Picture Soundtrack)", name);
        Assert.AreEqual("Deluxe Edition", version);
    }

    [TestMethod]
    public void SplitAlbumVersion_VersionWithSpecialCharacters_HandledCorrectly()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "OK Computer (OKNOTOK 1997 2017)", "OKNOTOK 1997 2017");

        Assert.AreEqual("OK Computer", name);
        Assert.AreEqual("OKNOTOK 1997 2017", version);
    }

    [TestMethod]
    public void SplitAlbumVersion_VersionEqualsFullName_DiscardsVersion()
    {
        // Edge case: version text is contained in the name (identical)
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Deluxe Edition", "Deluxe Edition");

        Assert.AreEqual("Deluxe Edition", name);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void SplitAlbumVersion_VersionWithParens_ExactSuffixMatch()
    {
        var (name, version) = AlbumMapper.SplitAlbumVersion(
            "Album (25th Anniversary Edition)", "25th Anniversary Edition");

        Assert.AreEqual("Album", name);
        Assert.AreEqual("25th Anniversary Edition", version);
    }

    // --- StripVersionSuffix ---

    [TestMethod]
    public void StripVersionSuffix_SuffixEmbedded_ReturnsShortName()
    {
        var result = AlbumMapper.StripVersionSuffix(
            "Abbey Road (Super Deluxe Edition)", "Super Deluxe Edition");

        Assert.AreEqual("Abbey Road", result);
    }

    [TestMethod]
    public void StripVersionSuffix_AlreadyClean_ReturnsUnchanged()
    {
        var result = AlbumMapper.StripVersionSuffix("Abbey Road", "Super Deluxe Edition");

        Assert.AreEqual("Abbey Road", result);
    }

    [TestMethod]
    public void StripVersionSuffix_NonStandard_ReturnsOriginal()
    {
        var result = AlbumMapper.StripVersionSuffix(
            "Abbey Road [Super Deluxe Edition]", "Super Deluxe Edition");

        Assert.AreEqual("Abbey Road [Super Deluxe Edition]", result);
    }

    [TestMethod]
    public void StripVersionSuffix_NullVersion_ReturnsOriginal()
    {
        var result = AlbumMapper.StripVersionSuffix("Abbey Road", null);

        Assert.AreEqual("Abbey Road", result);
    }

    [TestMethod]
    public void StripVersionSuffix_NullName_ReturnsEmpty()
    {
        var result = AlbumMapper.StripVersionSuffix(null, "Deluxe");

        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void StripVersionSuffix_BothNull_ReturnsEmpty()
    {
        var result = AlbumMapper.StripVersionSuffix(null, null);

        Assert.AreEqual(string.Empty, result);
    }
}
