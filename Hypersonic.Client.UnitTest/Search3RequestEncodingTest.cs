using Hypersonic.Client.Models.Requests;

namespace Hypersonic.Client.UnitTest;

[TestClass]
public class Search3RequestEncodingTest
{
    private static SubsonicRequest CreateBaseRequest()
    {
        return new SubsonicRequest("user", "pass", "test", "1.16.1", ResponseFormat.JSON);
    }

    [TestMethod]
    public void ToQueryString_EncodesSpacesInQuery()
    {
        var request = new Search3Request(CreateBaseRequest(), "hello world", null, null, null, null, null, null, null);
        var qs = request.ToQueryString();
        Assert.IsTrue(qs.Contains("&query=hello%20world"), $"Expected encoded spaces, got: {qs}");
    }

    [TestMethod]
    public void ToQueryString_EncodesAmpersandInQuery()
    {
        var request = new Search3Request(CreateBaseRequest(), "rock & roll", null, null, null, null, null, null, null);
        var qs = request.ToQueryString();
        Assert.IsTrue(qs.Contains("&query=rock%20%26%20roll"), $"Expected encoded ampersand, got: {qs}");
    }

    [TestMethod]
    public void ToQueryString_EncodesHashInQuery()
    {
        var request = new Search3Request(CreateBaseRequest(), "C# sharp", null, null, null, null, null, null, null);
        var qs = request.ToQueryString();
        Assert.IsTrue(qs.Contains("&query=C%23%20sharp"), $"Expected encoded hash, got: {qs}");
    }

    [TestMethod]
    public void ToQueryString_EncodesUnicodeInQuery()
    {
        var request = new Search3Request(CreateBaseRequest(), "été", null, null, null, null, null, null, null);
        var qs = request.ToQueryString();
        Assert.IsFalse(qs.Contains("&query=été"), $"Unicode should be encoded, got: {qs}");
        Assert.IsTrue(qs.Contains("&query=%C3%A9t%C3%A9"), $"Expected percent-encoded unicode, got: {qs}");
    }

    [TestMethod]
    public void ToQueryString_EmptyQueryProducesEmptyEncodedValue()
    {
        var request = new Search3Request(CreateBaseRequest(), "", null, null, null, null, null, null, null);
        var qs = request.ToQueryString();
        Assert.IsTrue(qs.Contains("&query=&") || qs.EndsWith("&query="), $"Expected empty query value, got: {qs}");
    }

    [TestMethod]
    public void ToQueryString_IncludesSongCountAndOffset()
    {
        var request = new Search3Request(CreateBaseRequest(), "test", null, null, null, null, 100, 50, null);
        var qs = request.ToQueryString();
        Assert.IsTrue(qs.Contains("&songCount=100"), $"Expected songCount, got: {qs}");
        Assert.IsTrue(qs.Contains("&songOffset=50"), $"Expected songOffset, got: {qs}");
    }
}
