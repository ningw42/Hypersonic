using Hypersonic.Services;

namespace Hypersonic.Tests.Services;

[TestClass]
public class ResolveServerUrlsTest
{
    [TestMethod]
    public void HostOnly_ReturnsHostWithRestThenWithout()
    {
        var urls = SubsonicClientService.ResolveServerUrls("example.com");
        Assert.AreEqual(2, urls.Length);
        Assert.AreEqual("https://example.com/rest", urls[0]);
        Assert.AreEqual("https://example.com", urls[1]);
    }

    [TestMethod]
    public void HostWithHttpsScheme_ReturnsHostWithRestThenWithout()
    {
        var urls = SubsonicClientService.ResolveServerUrls("https://example.com");
        Assert.AreEqual(2, urls.Length);
        Assert.AreEqual("https://example.com/rest", urls[0]);
        Assert.AreEqual("https://example.com", urls[1]);
    }

    [TestMethod]
    public void HostWithHttpScheme_PreservesHttp()
    {
        var urls = SubsonicClientService.ResolveServerUrls("http://example.com");
        Assert.AreEqual(2, urls.Length);
        Assert.AreEqual("http://example.com/rest", urls[0]);
        Assert.AreEqual("http://example.com", urls[1]);
    }

    [TestMethod]
    public void HostWithTrailingSlash_ReturnsHostWithRestThenWithout()
    {
        var urls = SubsonicClientService.ResolveServerUrls("https://example.com/");
        Assert.AreEqual(2, urls.Length);
        Assert.AreEqual("https://example.com/rest", urls[0]);
        Assert.AreEqual("https://example.com", urls[1]);
    }

    [TestMethod]
    public void HostWithRestPath_ReturnsSingleUrl()
    {
        var urls = SubsonicClientService.ResolveServerUrls("https://example.com/rest");
        Assert.AreEqual(1, urls.Length);
        Assert.AreEqual("https://example.com/rest", urls[0]);
    }

    [TestMethod]
    public void HostWithCustomPath_ReturnsSingleUrl()
    {
        var urls = SubsonicClientService.ResolveServerUrls("https://example.com/navidrome/rest");
        Assert.AreEqual(1, urls.Length);
        Assert.AreEqual("https://example.com/navidrome/rest", urls[0]);
    }

    [TestMethod]
    public void HostWithPort_ReturnsHostWithRestThenWithout()
    {
        var urls = SubsonicClientService.ResolveServerUrls("example.com:4533");
        Assert.AreEqual(2, urls.Length);
        Assert.AreEqual("https://example.com:4533/rest", urls[0]);
        Assert.AreEqual("https://example.com:4533", urls[1]);
    }

    [TestMethod]
    public void HostWithPortAndPath_ReturnsSingleUrl()
    {
        var urls = SubsonicClientService.ResolveServerUrls("http://localhost:4533/rest");
        Assert.AreEqual(1, urls.Length);
        Assert.AreEqual("http://localhost:4533/rest", urls[0]);
    }

    [TestMethod]
    public void InputWithWhitespace_IsTrimmed()
    {
        var urls = SubsonicClientService.ResolveServerUrls("  https://example.com  ");
        Assert.AreEqual(2, urls.Length);
        Assert.AreEqual("https://example.com/rest", urls[0]);
        Assert.AreEqual("https://example.com", urls[1]);
    }

    [TestMethod]
    public void TrailingSlashOnPath_IsStripped()
    {
        var urls = SubsonicClientService.ResolveServerUrls("https://example.com/rest/");
        Assert.AreEqual(1, urls.Length);
        Assert.AreEqual("https://example.com/rest", urls[0]);
    }
}
