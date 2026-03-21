using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Client.UnitTest.Deserialization;

[TestClass]
public class PingResponseTest
{
    public const string Navidrome_JSON_Ping_Success = """
        {
            "subsonic-response": {
                "status": "ok",
                "version": "1.16.1",
                "type": "navidrome",
                "serverVersion": "0.53.3 (v0.53.3)",
                "openSubsonic": true
            }
        }
        """;

    public const string Navidrome_XML_Ping_Success = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1" type="navidrome" serverVersion="0.53.3 (v0.53.3)" openSubsonic="true">
        </subsonic-response>
        """;

    public const string JSON_Ping_WrongCredentials = """
        {
            "subsonic-response": {
                "status": "failed",
                "version": "1.16.1",
                "type": "navidrome",
                "serverVersion": "0.53.3 (v0.53.3)",
                "openSubsonic": true,
                "error": {
                    "code": 40,
                    "message": "Wrong username or password"
                }
            }
        }
        """;

    public const string XML_Ping_WrongCredentials = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="failed" version="1.16.1" type="navidrome" serverVersion="0.53.3 (v0.53.3)" openSubsonic="true">
            <error code="40" message="Wrong username or password"></error>
        </subsonic-response>
        """;

    [TestMethod]
    public void Test_Navidrome_JSON_Ping_Success()
    {
        SubsonicResponse response = SubsonicClient.DeserializeJsonResponse<SubsonicResponse>(
            PingResponseTest.Navidrome_JSON_Ping_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual("ok", response.Status);
        Assert.AreEqual("1.16.1", response.Version);
        Assert.AreEqual("navidrome", response.Type);
        Assert.AreEqual("0.53.3 (v0.53.3)", response.ServerVersion);
        Assert.IsTrue(response.OpenSubsonic);
        Assert.IsNull(response.Error);
    }

    [TestMethod]
    public void Test_Navidrome_XML_Ping_Success()
    {
        SubsonicResponse response = SubsonicClient.DeserializeXmlResponse<SubsonicResponse>(
            PingResponseTest.Navidrome_XML_Ping_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual("ok", response.Status);
        Assert.AreEqual("1.16.1", response.Version);
        Assert.AreEqual("navidrome", response.Type);
        Assert.AreEqual("0.53.3 (v0.53.3)", response.ServerVersion);
        Assert.IsTrue(response.OpenSubsonic);
        Assert.IsNull(response.Error);
    }

    [TestMethod]
    public void Test_JSON_Ping_WrongCredentials()
    {
        SubsonicResponse response = SubsonicClient.DeserializeJsonResponse<SubsonicResponse>(
            PingResponseTest.JSON_Ping_WrongCredentials.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual("failed", response.Status);
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(40, response.Error.Code);
        Assert.AreEqual("Wrong username or password", response.Error.Message);
    }

    [TestMethod]
    public void Test_XML_Ping_WrongCredentials()
    {
        SubsonicResponse response = SubsonicClient.DeserializeXmlResponse<SubsonicResponse>(
            PingResponseTest.XML_Ping_WrongCredentials.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual("failed", response.Status);
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(40, response.Error.Code);
        Assert.AreEqual("Wrong username or password", response.Error.Message);
    }
}
