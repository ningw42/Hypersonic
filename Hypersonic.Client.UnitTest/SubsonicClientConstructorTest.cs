namespace Hypersonic.Client.UnitTest;

[TestClass]
public class SubsonicClientConstructorTest
{
    [TestMethod]
    public void Constructor_SetsAllProperties()
    {
        var httpClient = new HttpClient();
        var client = new SubsonicClient(
            httpClient,
            "https://example.com/rest",
            "testuser",
            "testpass",
            "1.16.1",
            "TestClient",
            ResponseFormat.JSON);

        Assert.AreSame(httpClient, client.HttpClient);
        Assert.AreEqual("https://example.com/rest", client.BaseUri);
        Assert.AreEqual("testuser", client.User);
        Assert.AreEqual("testpass", client.Password);
        Assert.AreEqual("1.16.1", client.ClientVersion);
        Assert.AreEqual("TestClient", client.ClientId);
        Assert.AreEqual(ResponseFormat.JSON, client.ResponseFormat);
    }

    [TestMethod]
    public void Constructor_DefaultsToXmlFormat()
    {
        var client = new SubsonicClient(
            new HttpClient(),
            "https://example.com/rest",
            "user",
            "pass",
            "1.16.1",
            "TestClient");

        Assert.AreEqual(ResponseFormat.XML, client.ResponseFormat);
    }
}
