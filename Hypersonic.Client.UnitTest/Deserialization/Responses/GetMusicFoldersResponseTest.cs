using Hypersonic.Client.Models.Responses;

namespace Hypersonic.Client.UnitTest.Deserialization;

[TestClass]
public sealed class GetMusicFoldersResponseTest
{
    public const string Navidrome_JSON_Response_Success = """
        {
          "subsonic-response": {
            "status": "ok",
            "version": "1.16.1",
            "type": "navidrome",
            "serverVersion": "0.53.3 (v0.53.3)",
            "openSubsonic": true,
            "musicFolders": {
              "musicFolder": [
                {
                  "id": 1,
                  "name": "Music Library"
                }
              ]
            }
          }
        }
        """;

    public const string Navidrome_XML_Response_Success = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1" type="navidrome" serverVersion="0.53.3 (v0.53.3)" openSubsonic="true">
          <musicFolders>
            <musicFolder id="1" name="Music Library"/>
          </musicFolders>
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
            "musicFolders": {
              "musicFolder": [
                {
                  "id": "1",
                  "name": "music"
                },
                {
                  "id": "4",
                  "name": "upload"
                }
              ]
            }
          }
        }
        """;

    public const string OpenSubsonic_XML_Response_Success = """
        <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1" type="AwesomeServerName" serverVersion="0.1.3 (tag)" openSubsonic="true">
          <musicFolders>
            <musicFolder id="1" name="music"/>
            <musicFolder id="4" name="upload"/>
          </musicFolders>
        </subsonic-response>
        """;

    [TestMethod]
    public void Test_Navidrome_JSON_Success()
    {
        GetMusicFoldersResponse response = SubsonicClient.DeserializeJsonResponse<GetMusicFoldersResponse>(GetMusicFoldersResponseTest.Navidrome_JSON_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(1, response.MusicFolders.MusicFolder.Length);
        Assert.AreEqual("1", response.MusicFolders.MusicFolder[0].Id);
        Assert.AreEqual("Music Library", response.MusicFolders.MusicFolder[0].Name);
    }

    [TestMethod]
    public void Test_Navidrome_XML_Success()
    {
        GetMusicFoldersResponse response = SubsonicClient.DeserializeXmlResponse<GetMusicFoldersResponse>(GetMusicFoldersResponseTest.Navidrome_XML_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(1, response.MusicFolders.MusicFolder.Length);
        Assert.AreEqual("1", response.MusicFolders.MusicFolder[0].Id);
        Assert.AreEqual("Music Library", response.MusicFolders.MusicFolder[0].Name);
    }

    [TestMethod]
    public void Test_OpenSubsonic_JSON_Success()
    {
        GetMusicFoldersResponse response = SubsonicClient.DeserializeJsonResponse<GetMusicFoldersResponse>(GetMusicFoldersResponseTest.OpenSubsonic_JSON_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(2, response.MusicFolders.MusicFolder.Length);
        Assert.AreEqual("1", response.MusicFolders.MusicFolder[0].Id);
        Assert.AreEqual("music", response.MusicFolders.MusicFolder[0].Name);
        Assert.AreEqual("4", response.MusicFolders.MusicFolder[1].Id);
        Assert.AreEqual("upload", response.MusicFolders.MusicFolder[1].Name);
    }

    [TestMethod]
    public void Test_OpenSubsonic_XML_Success()
    {
        GetMusicFoldersResponse response = SubsonicClient.DeserializeXmlResponse<GetMusicFoldersResponse>(GetMusicFoldersResponseTest.OpenSubsonic_XML_Response_Success.ToStream());
        Assert.IsNotNull(response);
        Assert.AreEqual(2, response.MusicFolders.MusicFolder.Length);
        Assert.AreEqual("1", response.MusicFolders.MusicFolder[0].Id);
        Assert.AreEqual("music", response.MusicFolders.MusicFolder[0].Name);
        Assert.AreEqual("4", response.MusicFolders.MusicFolder[1].Id);
        Assert.AreEqual("upload", response.MusicFolders.MusicFolder[1].Name);
    }
}
