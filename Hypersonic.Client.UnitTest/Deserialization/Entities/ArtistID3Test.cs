using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.UnitTest.Deserialization;

[TestClass]
public class ArtistID3Test
{
    public const string OpenSubsonic_Example_JSON = """
        {
            "id": "37ec820ca7193e17040c98f7da7c4b51",
            "name": "2 Mello",
            "coverArt": "ar-37ec820ca7193e17040c98f7da7c4b51_0",
            "albumCount": 1,
            "userRating": 5,
            "artistImageUrl": "https://demo.org/image.jpg",
            "starred": "2017-04-11T10:42:50.842Z",
            "musicBrainzId": "189002e7-3285-4e2e-92a3-7f6c30d407a2",
            "sortName": "Mello (2)",
            "roles": [
                "artist",
                "albumartist",
                "composer"
            ]
        }
        """;

    [TestMethod]
    public void Test_OpenSubsonic_Example_JSON()
    {
        ArtistID3 artistId3 = SubsonicClient.DeserializeJson<ArtistID3>(ArtistID3Test.OpenSubsonic_Example_JSON.ToStream());
        Assert.IsNotNull(artistId3);
        Assert.AreEqual("37ec820ca7193e17040c98f7da7c4b51", artistId3.Id);
        Assert.AreEqual("2 Mello", artistId3.Name);
        Assert.AreEqual("ar-37ec820ca7193e17040c98f7da7c4b51_0", artistId3.CoverArt);
        Assert.AreEqual(1, artistId3.AlbumCount);
        Assert.AreEqual(5, artistId3.UserRating);
        Assert.AreEqual("https://demo.org/image.jpg", artistId3.ArtistImageUrl);
        Assert.AreEqual("2017-04-11T10:42:50.842Z", artistId3.Starred);
        Assert.AreEqual("189002e7-3285-4e2e-92a3-7f6c30d407a2", artistId3.MusicBrainzId);
        Assert.AreEqual("Mello (2)", artistId3.SortName);
        Assert.AreEqual(3, artistId3.Roles.Length);
        Assert.AreEqual("artist", artistId3.Roles[0]);
        Assert.AreEqual("albumartist", artistId3.Roles[1]);
        Assert.AreEqual("composer", artistId3.Roles[2]);
    }
}
