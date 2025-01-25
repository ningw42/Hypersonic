using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Client.UnitTest.Deserialization;

[TestClass]
public class SubsonicResponseTest
{
    public const string Navidrome_JSON_Response_Success = """
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

    public const string Navidrome_XML_Response_Success = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1" type="navidrome" serverVersion="0.53.3 (v0.53.3)" openSubsonic="true">
        </subsonic-response>
        """;

    public const string Navidrome_JSON_Response_Failure_70 = """
        {
            "subsonic-response": {
                "status": "failed",
                "version": "1.16.1",
                "type": "navidrome",
                "serverVersion": "0.53.3 (v0.53.3)",
                "openSubsonic": true,
                "error": {
                    "code": 70,
                    "message": "Directory not found"
                }
            }
        }
        """;

    public const string Navidrome_XML_Response_Failure_70 = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="failed" version="1.16.1" type="navidrome" serverVersion="0.53.3 (v0.53.3)" openSubsonic="true">
            <error code="70" message="Directory not found"></error>
        </subsonic-response>
        """;

    public const string OpenSubsonic_JSON_Response_Failure_42 = """
        {
            "subsonic-response": {
                "status": "failed",
                "version": "1.16.1",
                "type": "AwesomeServerName",
                "serverVersion": "0.1.3 (tag)",
                "openSubsonic": true,
                "error": {
                    "code": 42,
                    "message": "Authentication mechanism not supported. Use API keys",
                    "helpUrl": "https://example.org/users/apiKey"
                }
            }
        }
        """;

    public const string OpenSubsonic_XML_Response_Failure_42 = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="failed" version="1.16.1" type="AwesomeServerName" serverVersion="0.1.3 (tag)" openSubsonic="true">
            <error code="42" message="Authentication mechanism not supported. Use API keys" helpUrl="https://example.org/users/apiKey"></error>
        </subsonic-response>
        """;

    [TestMethod]
    public void Test_Navidrome_JSON_Success()
    {
        SubsonicResponse response = SubsonicClient.DeserializeJsonResponse<SubsonicResponse>(SubsonicResponseTest.Navidrome_JSON_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual("ok", response.Status);
        Assert.AreEqual("1.16.1", response.Version);
        Assert.AreEqual("navidrome", response.Type);
        Assert.AreEqual("0.53.3 (v0.53.3)", response.ServerVersion);
        Assert.AreEqual(true, response.OpenSubsonic);
        Assert.IsNull(response.Error);
    }

    [TestMethod]
    public void Test_Navidrome_XML_Success()
    {
        SubsonicResponse response = SubsonicClient.DeserializeXmlResponse<SubsonicResponse>(SubsonicResponseTest.Navidrome_XML_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual("ok", response.Status);
        Assert.AreEqual("1.16.1", response.Version);
        Assert.AreEqual("navidrome", response.Type);
        Assert.AreEqual("0.53.3 (v0.53.3)", response.ServerVersion);
        Assert.AreEqual(true, response.OpenSubsonic);
        Assert.IsNull(response.Error);
    }

    [TestMethod]
    public void Test_Navidrome_JSON_Failure_70()
    {
        SubsonicResponse response = SubsonicClient.DeserializeJsonResponse<SubsonicResponse>(SubsonicResponseTest.Navidrome_JSON_Response_Failure_70.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual("failed", response.Status);
        Assert.AreEqual("1.16.1", response.Version);
        Assert.AreEqual("navidrome", response.Type);
        Assert.AreEqual("0.53.3 (v0.53.3)", response.ServerVersion);
        Assert.AreEqual(true, response.OpenSubsonic);
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(70, response.Error.Code);
        Assert.AreEqual("Directory not found", response.Error.Message);
        Assert.AreEqual(null, response.Error.HelpUrl);
    }

    [TestMethod]
    public void Test_Navidrome_XML_Failure_70()
    {
        SubsonicResponse response = SubsonicClient.DeserializeXmlResponse<SubsonicResponse>(SubsonicResponseTest.Navidrome_XML_Response_Failure_70.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual("failed", response.Status);
        Assert.AreEqual("1.16.1", response.Version);
        Assert.AreEqual("navidrome", response.Type);
        Assert.AreEqual("0.53.3 (v0.53.3)", response.ServerVersion);
        Assert.AreEqual(true, response.OpenSubsonic);
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(70, response.Error.Code);
        Assert.AreEqual("Directory not found", response.Error.Message);
        Assert.AreEqual(null, response.Error.HelpUrl);
    }

    [TestMethod]
    public void Test_Navidrome_JSON_Failure_42()
    {
        SubsonicResponse response = SubsonicClient.DeserializeJsonResponse<SubsonicResponse>(SubsonicResponseTest.OpenSubsonic_JSON_Response_Failure_42.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual("failed", response.Status);
        Assert.AreEqual("1.16.1", response.Version);
        Assert.AreEqual("AwesomeServerName", response.Type);
        Assert.AreEqual("0.1.3 (tag)", response.ServerVersion);
        Assert.AreEqual(true, response.OpenSubsonic);
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(42, response.Error.Code);
        Assert.AreEqual("Authentication mechanism not supported. Use API keys", response.Error.Message);
        Assert.AreEqual("https://example.org/users/apiKey", response.Error.HelpUrl);
    }

    [TestMethod]
    public void Test_Navidrome_XML_Failure_42()
    {
        SubsonicResponse response = SubsonicClient.DeserializeXmlResponse<SubsonicResponse>(SubsonicResponseTest.OpenSubsonic_XML_Response_Failure_42.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual("failed", response.Status);
        Assert.AreEqual("1.16.1", response.Version);
        Assert.AreEqual("AwesomeServerName", response.Type);
        Assert.AreEqual("0.1.3 (tag)", response.ServerVersion);
        Assert.AreEqual(true, response.OpenSubsonic);
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(42, response.Error.Code);
        Assert.AreEqual("Authentication mechanism not supported. Use API keys", response.Error.Message);
        Assert.AreEqual("https://example.org/users/apiKey", response.Error.HelpUrl);
    }
}
