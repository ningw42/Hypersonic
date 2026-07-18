using System.Text.Json;

using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;

namespace Hypersonic.Core.Tests.Models;

[TestClass]
public class PersistenceTests
{
    // --- Vault Key Derivation ---

    [TestMethod]
    public void VaultKeyDerivation_FullUrl()
    {
        var key = VaultKeyHelper.GetVaultKey("https://music.example.com/rest");
        Assert.AreEqual("Hypersonic:https://music.example.com/rest", key);
    }

    [TestMethod]
    public void VaultKeyDerivation_HttpWithPort()
    {
        var key = VaultKeyHelper.GetVaultKey("http://192.168.1.50:4533/");
        Assert.AreEqual("Hypersonic:http://192.168.1.50:4533/", key);
    }

    [TestMethod]
    public void VaultKeyDerivation_DifferentSchemes_DifferentKeys()
    {
        var httpKey = VaultKeyHelper.GetVaultKey("http://host");
        var httpsKey = VaultKeyHelper.GetVaultKey("https://host");
        Assert.AreNotEqual(httpKey, httpsKey);
    }

    [TestMethod]
    public void VaultKeyDerivation_PathBasedUrls_DifferentKeys()
    {
        var key1 = VaultKeyHelper.GetVaultKey("https://host/navidrome1");
        var key2 = VaultKeyHelper.GetVaultKey("https://host/navidrome2");
        Assert.AreNotEqual(key1, key2);
    }

    // --- JSON Serialization ---

    [TestMethod]
    public async Task JsonHelper_SerializeDeserialize_RoundTrip()
    {
        var original = new ServerAccount
        {
            ServerUrl = "https://music.example.com/rest",
            Username = "admin",
        };

        var json = await Json.StringifyAsync(original);
        Assert.IsNotNull(json);
        Assert.IsTrue(json.Contains("music.example.com"));

        var deserialized = await Json.ToObjectAsync<ServerAccount>(json);
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.ServerUrl, deserialized!.ServerUrl);
        Assert.AreEqual(original.Username, deserialized.Username);
    }

    [TestMethod]
    public async Task JsonHelper_ReadsLegacyNewtonsoftOutput()
    {
        // Newtonsoft.Json default output for a ServerAccount-like object.
        var legacyJson = """{"ServerUrl":"https://music.example.com/rest","Username":"admin"}""";

        var deserialized = await Json.ToObjectAsync<ServerAccount>(legacyJson);
        Assert.IsNotNull(deserialized);
        Assert.AreEqual("https://music.example.com/rest", deserialized!.ServerUrl);
        Assert.AreEqual("admin", deserialized.Username);
    }

    // --- AppState ---

    [TestMethod]
    public void AppState_Defaults()
    {
        var state = new AppState();

        Assert.AreEqual(100, state.Playback.Volume);
        Assert.IsFalse(state.Playback.IsMuted);
        Assert.IsFalse(state.Playback.ShuffleEnabled);
        Assert.AreEqual(RepeatMode.Off, state.Playback.RepeatMode);

        Assert.IsNull(state.Window.Placement);

        Assert.IsNull(state.Navigation.LastPage);

        Assert.IsFalse(state.General.MinimizeToTray);
        Assert.IsFalse(state.General.CloseToTray);
    }

    [TestMethod]
    public void AppState_JsonRoundTrip()
    {
        var state = new AppState
        {
            Playback = new PlaybackState
            {
                Volume = 75,
                IsMuted = true,
                ShuffleEnabled = true,
                RepeatMode = RepeatMode.All,
            },
            Window = new WindowState
            {
                Placement = "AQIDBAUG==",
            },
            Navigation = new NavigationState
            {
                LastPage = "Hypersonic.ViewModels.AlbumsViewModel",
            },
            General = new GeneralState
            {
                MinimizeToTray = true,
                CloseToTray = true,
            },
        };

        var json = JsonSerializer.Serialize(state, Json.Options);
        Assert.IsNotNull(json);
        Assert.IsTrue(json.Contains("\"RepeatMode\": \"All\""));

        var deserialized = JsonSerializer.Deserialize<AppState>(json, Json.Options);
        Assert.IsNotNull(deserialized);

        Assert.AreEqual(75, deserialized!.Playback.Volume);
        Assert.IsTrue(deserialized.Playback.IsMuted);
        Assert.IsTrue(deserialized.Playback.ShuffleEnabled);
        Assert.AreEqual(RepeatMode.All, deserialized.Playback.RepeatMode);

        Assert.AreEqual("AQIDBAUG==", deserialized.Window.Placement);

        Assert.AreEqual("Hypersonic.ViewModels.AlbumsViewModel", deserialized.Navigation.LastPage);
        Assert.IsTrue(deserialized.General.MinimizeToTray);
        Assert.IsTrue(deserialized.General.CloseToTray);
    }

    [TestMethod]
    public void AppState_LegacyWindowGeometry_IsIgnored()
    {
        var legacyJson = """
            {
              "Playback": {
                "Volume": 42,
                "IsMuted": true,
                "ShuffleEnabled": false,
                "RepeatMode": "One"
              },
              "Window": {
                "Width": 1200,
                "Height": 800,
                "X": 100,
                "Y": 200,
                "IsMaximized": true
              },
              "Navigation": {
                "LastPage": "Hypersonic.ViewModels.TracksViewModel"
              },
              "General": {
                "MinimizeToTray": true,
                "CloseToTray": false
              }
            }
            """;

        var deserialized = JsonSerializer.Deserialize<AppState>(legacyJson, Json.Options);

        Assert.IsNotNull(deserialized);
        Assert.IsNull(deserialized!.Window.Placement);
        Assert.AreEqual(42, deserialized.Playback.Volume);
        Assert.IsTrue(deserialized.Playback.IsMuted);
        Assert.AreEqual(RepeatMode.One, deserialized.Playback.RepeatMode);
        Assert.AreEqual("Hypersonic.ViewModels.TracksViewModel", deserialized.Navigation.LastPage);
        Assert.IsTrue(deserialized.General.MinimizeToTray);
        Assert.IsFalse(deserialized.General.CloseToTray);

        var migratedJson = JsonSerializer.Serialize(deserialized, Json.Options);
        Assert.IsFalse(migratedJson.Contains("\"Width\"", StringComparison.Ordinal));
        Assert.IsFalse(migratedJson.Contains("\"Height\"", StringComparison.Ordinal));
        Assert.IsFalse(migratedJson.Contains("\"X\"", StringComparison.Ordinal));
        Assert.IsFalse(migratedJson.Contains("\"Y\"", StringComparison.Ordinal));
        Assert.IsFalse(migratedJson.Contains("\"IsMaximized\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AppState_RepeatMode_SerializesAsString()
    {
        var state = new AppState
        {
            Playback = new PlaybackState { RepeatMode = RepeatMode.One },
        };

        var json = JsonSerializer.Serialize(state, Json.Options);
        Assert.IsTrue(json.Contains("\"One\""));
        Assert.IsFalse(json.Contains("\"2\"")); // Should NOT be integer
    }
}
