using Hypersonic.Core.Models;

namespace Hypersonic.Core.Tests.Models;

[TestClass]
public class AlbumInfoTests
{
    // --- DisplayName ---

    [TestMethod]
    public void DisplayName_WithVersion_ReturnsNameAndVersion()
    {
        var album = new AlbumInfo { Name = "Abbey Road", Version = "Super Deluxe Edition" };

        Assert.AreEqual("Abbey Road (Super Deluxe Edition)", album.DisplayName);
    }

    [TestMethod]
    public void DisplayName_WithoutVersion_ReturnsNameOnly()
    {
        var album = new AlbumInfo { Name = "Abbey Road" };

        Assert.AreEqual("Abbey Road", album.DisplayName);
    }

    [TestMethod]
    public void DisplayName_NullVersion_ReturnsNameOnly()
    {
        var album = new AlbumInfo { Name = "Abbey Road", Version = null };

        Assert.AreEqual("Abbey Road", album.DisplayName);
    }

    [TestMethod]
    public void DisplayName_EmptyVersion_ReturnsNameOnly()
    {
        var album = new AlbumInfo { Name = "Abbey Road", Version = "" };

        Assert.AreEqual("Abbey Road", album.DisplayName);
    }

    [TestMethod]
    public void DisplayName_EmptyName_WithVersion_ReturnsVersionInParens()
    {
        var album = new AlbumInfo { Name = "", Version = "Deluxe" };

        Assert.AreEqual(" (Deluxe)", album.DisplayName);
    }

    [TestMethod]
    public void DisplayName_SingleWordVersion_FormatsCorrectly()
    {
        var album = new AlbumInfo { Name = "Thriller", Version = "Remastered" };

        Assert.AreEqual("Thriller (Remastered)", album.DisplayName);
    }

    [TestMethod]
    public void DisplayName_DefaultState_ReturnsEmptyString()
    {
        var album = new AlbumInfo();

        Assert.AreEqual(string.Empty, album.DisplayName);
    }

    // --- Version property ---

    [TestMethod]
    public void Version_DefaultsToNull()
    {
        var album = new AlbumInfo();

        Assert.IsNull(album.Version);
    }

    [TestMethod]
    public void Version_CanBeSetAndRead()
    {
        var album = new AlbumInfo { Version = "Deluxe Edition" };

        Assert.AreEqual("Deluxe Edition", album.Version);
    }
}
