using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Client.UnitTest.Deserialization;

[TestClass]
public class GetOpenSubsonicExtensionsResponseTest
{
    public const string Navidrome_JSON_Response_Success = """
        {
            "subsonic-response": {
                "status": "ok",
                "version": "1.16.1",
                "type": "navidrome",
                "serverVersion": "0.53.3 (v0.53.3)",
                "openSubsonic": true,
                "openSubsonicExtensions": [
                    {
                        "name": "transcodeOffset",
                        "versions": [
                            1
                        ]
                    },
                    {
                        "name": "formPost",
                        "versions": [
                            1
                        ]
                    },
                    {
                        "name": "songLyrics",
                        "versions": [
                            1
                        ]
                    }
                ]
            }
        }
        """;

    public const string Navidrome_XML_Response_Success = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1" type="navidrome" serverVersion="0.53.3 (v0.53.3)" openSubsonic="true">
            <openSubsonicExtensions name="transcodeOffset">
                <versions>1</versions>
            </openSubsonicExtensions>
            <openSubsonicExtensions name="formPost">
                <versions>1</versions>
            </openSubsonicExtensions>
            <openSubsonicExtensions name="songLyrics">
                <versions>1</versions>
            </openSubsonicExtensions>
        </subsonic-response>
        """;

    public const string OpenSubsonic_JSON_Response_Success = """
        {
            "subsonic-response": {
                "status": "ok",
                "version": "1.16.1",
                "type": "AwesomeServerName",
                "serverVersion": "0.1.3 (tag)",
                "openSubsonic": true,
                "openSubsonicExtensions": [
                    {
                        "name": "template",
                        "versions": [
                            1,
                            2
                        ]
                    },
                    {
                        "name": "transcodeOffset",
                        "versions": [
                            1
                        ]
                    }
                ]
            }
        }
        """;

    public const string OpenSubsonic_XML_Response_Success = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1" type="AwesomeServerName" serverVersion="0.1.3 (tag)" openSubsonic="true">
            <openSubsonicExtensions name="template">
                <versions>1</versions>
                <versions>2</versions>
            </openSubsonicExtensions>
            <openSubsonicExtensions name="transcodeOffset">
                <versions>1</versions>
            </openSubsonicExtensions>
        </subsonic-response>
        """;

    [TestMethod]
    public void Test_Navidrome_JSON_Success()
    {
        GetOpenSubsonicExtensionsResponse response = SubsonicClient.DeserializeJsonResponse<GetOpenSubsonicExtensionsResponse>(GetOpenSubsonicExtensionsResponseTest.Navidrome_JSON_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(3, response.OpenSubsonicExtensions.Length);
        Assert.AreEqual("transcodeOffset", response.OpenSubsonicExtensions[0].Name);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[0].Versions.Length);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[0].Versions[0]);
        Assert.AreEqual("formPost", response.OpenSubsonicExtensions[1].Name);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[1].Versions.Length);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[1].Versions[0]);
        Assert.AreEqual("songLyrics", response.OpenSubsonicExtensions[2].Name);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[2].Versions.Length);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[2].Versions[0]);
    }

    [TestMethod]
    public void Test_Navidrome_XML_Success()
    {
        GetOpenSubsonicExtensionsResponse response = SubsonicClient.DeserializeXmlResponse<GetOpenSubsonicExtensionsResponse>(GetOpenSubsonicExtensionsResponseTest.Navidrome_XML_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(3, response.OpenSubsonicExtensions.Length);
        Assert.AreEqual("transcodeOffset", response.OpenSubsonicExtensions[0].Name);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[0].Versions.Length);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[0].Versions[0]);
        Assert.AreEqual("formPost", response.OpenSubsonicExtensions[1].Name);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[1].Versions.Length);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[1].Versions[0]);
        Assert.AreEqual("songLyrics", response.OpenSubsonicExtensions[2].Name);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[2].Versions.Length);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[2].Versions[0]);
    }

    [TestMethod]
    public void Test_Subsonic_JSON_Success()
    {
        GetOpenSubsonicExtensionsResponse response = SubsonicClient.DeserializeJsonResponse<GetOpenSubsonicExtensionsResponse>(GetOpenSubsonicExtensionsResponseTest.OpenSubsonic_JSON_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(2, response.OpenSubsonicExtensions.Length);
        Assert.AreEqual("template", response.OpenSubsonicExtensions[0].Name);
        Assert.AreEqual(2, response.OpenSubsonicExtensions[0].Versions.Length);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[0].Versions[0]);
        Assert.AreEqual(2, response.OpenSubsonicExtensions[0].Versions[1]);
        Assert.AreEqual("transcodeOffset", response.OpenSubsonicExtensions[1].Name);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[1].Versions.Length);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[1].Versions[0]);
    }

    [TestMethod]
    public void Test_Subsonic_XML_Success()
    {
        GetOpenSubsonicExtensionsResponse response = SubsonicClient.DeserializeXmlResponse<GetOpenSubsonicExtensionsResponse>(GetOpenSubsonicExtensionsResponseTest.OpenSubsonic_XML_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(2, response.OpenSubsonicExtensions.Length);
        Assert.AreEqual("template", response.OpenSubsonicExtensions[0].Name);
        Assert.AreEqual(2, response.OpenSubsonicExtensions[0].Versions.Length);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[0].Versions[0]);
        Assert.AreEqual(2, response.OpenSubsonicExtensions[0].Versions[1]);
        Assert.AreEqual("transcodeOffset", response.OpenSubsonicExtensions[1].Name);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[1].Versions.Length);
        Assert.AreEqual(1, response.OpenSubsonicExtensions[1].Versions[0]);
    }
}
