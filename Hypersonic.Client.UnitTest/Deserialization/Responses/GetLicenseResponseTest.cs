using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Client.UnitTest.Deserialization;

[TestClass]
public class GetLicenseResponseTest
{
    public const string Navidrome_XML_Response_Success = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1" type="navidrome" serverVersion="0.53.3 (v0.53.3)" openSubsonic="true">
            <license valid="true"/>
        </subsonic-response>
        """;

    public const string Navidrome_JSON_Response_Success = """
        {
            "subsonic-response": {
                "status": "ok",
                "version": "1.16.1",
                "type": "navidrome",
                "serverVersion": "0.53.3 (v0.53.3)",
                "openSubsonic": true,
                "license": {
                    "valid": true
                }
            }
        }
        """;

    public const string OpenSubsonic_XML_Response_Success = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1" type="AwesomeServerName" serverVersion="0.1.3 (tag)" openSubsonic="true">
            <license valid="true" email="demo@demo.org" licenseExpires="2017-04-11T10:42:50.842Z" trialExpires="2017-04-11T10:42:50.842Z"/>
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
                "license": {
                    "valid": true,
                    "email": "demo@demo.org",
                    "licenseExpires": "2017-04-11T10:42:50.842Z",
                    "trialExpires": "2017-04-11T10:42:50.842Z"
                }
            }
        }
        """;

    [TestMethod]
    public void Test_Navidrome_JSON_Success()
    {
        GetLicenseResponse response = SubsonicClient.DeserializeJsonResponse<GetLicenseResponse>(GetLicenseResponseTest.Navidrome_JSON_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(true, response.License.Valid);
    }

    [TestMethod]
    public void Test_Navidrome_XML_Success()
    {
        GetLicenseResponse response = SubsonicClient.DeserializeJsonResponse<GetLicenseResponse>(GetLicenseResponseTest.Navidrome_JSON_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(true, response.License.Valid);
    }

    [TestMethod]
    public void Test_OpenSubsonic_JSON_Success()
    {
        GetLicenseResponse response = SubsonicClient.DeserializeJsonResponse<GetLicenseResponse>(GetLicenseResponseTest.OpenSubsonic_JSON_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(true, response.License.Valid);
        Assert.AreEqual("demo@demo.org", response.License.Email);
        Assert.AreEqual("2017-04-11T10:42:50.842Z", response.License.LicenseExpires);
        Assert.AreEqual("2017-04-11T10:42:50.842Z", response.License.TrialExpires);
    }

    [TestMethod]
    public void Test_OpenSubsonic_XML_Success()
    {
        GetLicenseResponse response = SubsonicClient.DeserializeXmlResponse<GetLicenseResponse>(GetLicenseResponseTest.OpenSubsonic_XML_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(true, response.License.Valid);
        Assert.AreEqual("demo@demo.org", response.License.Email);
        Assert.AreEqual("2017-04-11T10:42:50.842Z", response.License.LicenseExpires);
        Assert.AreEqual("2017-04-11T10:42:50.842Z", response.License.TrialExpires);
    }
}
