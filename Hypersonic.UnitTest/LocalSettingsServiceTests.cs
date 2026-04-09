using System.Text;
using System.Text.Json;

using Hypersonic.Contracts.Services;
using Hypersonic.Helpers;
using Hypersonic.Models;
using Hypersonic.Services;

using Microsoft.Extensions.Options;

namespace Hypersonic.UnitTest;

[TestClass]
public class LocalSettingsServiceTests
{
    private string _tempDir = null!;
    private string _settingsFolder = null!;
    private const string SettingsFile = "LocalSettings.json";

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hypersonic-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // LocalSettingsService prepends %LOCALAPPDATA% to the folder option,
        // so compute a relative path that resolves back to our temp directory.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsFolder = Path.GetRelativePath(localAppData, _tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private LocalSettingsService CreateService()
    {
        var fileService = new FileService();
        var options = new TestOptions<LocalSettingsOptions>(new LocalSettingsOptions
        {
            ApplicationDataFolder = _settingsFolder,
            LocalSettingsFile = SettingsFile,
        });
        var logService = new LoggingService();
        return new LocalSettingsService(fileService, options, logService);
    }

    // --- Save and Read round-trips ---

    [TestMethod]
    public async Task SaveAndRead_String()
    {
        var service = CreateService();

        await service.SaveSettingAsync("Theme", "Light");
        var result = await service.ReadSettingAsync<string>("Theme");

        Assert.AreEqual("Light", result);
    }

    [TestMethod]
    public async Task SaveAndRead_ComplexType()
    {
        var service = CreateService();
        var accounts = new List<ServerAccount>
        {
            new() { ServerUrl = "https://music.example.com/rest", Username = "admin" },
            new() { ServerUrl = "https://other.example.com/rest", Username = "user2" },
        };

        await service.SaveSettingAsync("ServerAccounts", accounts);
        var result = await service.ReadSettingAsync<List<ServerAccount>>("ServerAccounts");

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result!.Count);
        Assert.AreEqual("https://music.example.com/rest", result[0].ServerUrl);
        Assert.AreEqual("admin", result[0].Username);
        Assert.AreEqual("https://other.example.com/rest", result[1].ServerUrl);
        Assert.AreEqual("user2", result[1].Username);
    }

    [TestMethod]
    public async Task SaveProducesCleanJson()
    {
        var service = CreateService();
        var accounts = new List<ServerAccount>
        {
            new() { ServerUrl = "https://music.example.com/rest", Username = "admin" },
        };

        await service.SaveSettingAsync("Theme", "Light");
        await service.SaveSettingAsync("ServerAccounts", accounts);

        var rawJson = File.ReadAllText(Path.Combine(_tempDir, SettingsFile));

        // Should NOT contain escaped quotes (the hallmark of double-serialization)
        Assert.IsFalse(rawJson.Contains("\\u0022"), "File contains escaped unicode quotes — double serialization detected");
        Assert.IsFalse(rawJson.Contains("\\\""), "File contains escaped quotes — double serialization detected");

        // Should contain clean structured JSON
        Assert.IsTrue(rawJson.Contains("\"Theme\": \"Light\""));
        Assert.IsTrue(rawJson.Contains("\"ServerUrl\": \"https://music.example.com/rest\""));
    }

    // --- Migration of double-serialized files ---

    [TestMethod]
    public async Task Migration_DoubleSerializedString_IsUnwrapped()
    {
        // Write a legacy file with double-serialized string value
        var legacyContent = """
            {
              "AppBackgroundRequestedTheme": "\u0022Light\u0022"
            }
            """;
        File.WriteAllText(Path.Combine(_tempDir, SettingsFile), legacyContent, Encoding.UTF8);

        var service = CreateService();
        var result = await service.ReadSettingAsync<string>("AppBackgroundRequestedTheme");

        Assert.AreEqual("Light", result);

        // File should have been rewritten to clean format
        var rawJson = File.ReadAllText(Path.Combine(_tempDir, SettingsFile));
        Assert.IsTrue(rawJson.Contains("\"AppBackgroundRequestedTheme\": \"Light\""));
        Assert.IsFalse(rawJson.Contains("\\u0022"));
    }

    [TestMethod]
    public async Task Migration_DoubleSerializedArray_IsUnwrapped()
    {
        // Write a legacy file with a stringified JSON array
        var accountJson = JsonSerializer.Serialize(new List<ServerAccount>
        {
            new() { ServerUrl = "https://music.example.com/rest", Username = "admin" },
        }, Json.Options);
        var legacyDict = new Dictionary<string, string>
        {
            ["ServerAccounts"] = accountJson,
        };
        var legacyContent = JsonSerializer.Serialize(legacyDict, Json.Options);
        File.WriteAllText(Path.Combine(_tempDir, SettingsFile), legacyContent, Encoding.UTF8);

        var service = CreateService();
        var result = await service.ReadSettingAsync<List<ServerAccount>>("ServerAccounts");

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result!.Count);
        Assert.AreEqual("https://music.example.com/rest", result[0].ServerUrl);
        Assert.AreEqual("admin", result[0].Username);

        // File should have been rewritten to clean format
        var rawJson = File.ReadAllText(Path.Combine(_tempDir, SettingsFile));
        Assert.IsFalse(rawJson.Contains("\\u0022"));
        Assert.IsTrue(rawJson.Contains("\"ServerUrl\": \"https://music.example.com/rest\""));
    }

    [TestMethod]
    public async Task Migration_UnknownKey_IsNotTouched()
    {
        // A key not in the legacy allowlist should not be migrated, even if
        // its string value happens to be valid JSON.
        var legacyContent = """
            {
              "CustomKey": "\u0022true\u0022"
            }
            """;
        File.WriteAllText(Path.Combine(_tempDir, SettingsFile), legacyContent, Encoding.UTF8);

        var service = CreateService();
        var result = await service.ReadSettingAsync<string>("CustomKey");

        // The raw string includes literal quotes because migration did not unwrap it
        Assert.AreEqual("\"true\"", result);
    }

    [TestMethod]
    public async Task Migration_AlreadyClean_DoesNotRewrite()
    {
        // Save clean settings first
        var service1 = CreateService();
        await service1.SaveSettingAsync("AppBackgroundRequestedTheme", "Light");
        await service1.SaveSettingAsync("ServerAccounts", new List<ServerAccount>
        {
            new() { ServerUrl = "https://music.example.com/rest", Username = "admin" },
        });

        var contentAfterSave = File.ReadAllText(Path.Combine(_tempDir, SettingsFile));
        var modifiedTimeAfterSave = File.GetLastWriteTimeUtc(Path.Combine(_tempDir, SettingsFile));

        // Small delay to detect if file gets rewritten
        await Task.Delay(50);

        // New service instance reads the clean file — migration should be a no-op
        var service2 = CreateService();
        var theme = await service2.ReadSettingAsync<string>("AppBackgroundRequestedTheme");

        Assert.AreEqual("Light", theme);

        var modifiedTimeAfterRead = File.GetLastWriteTimeUtc(Path.Combine(_tempDir, SettingsFile));
        Assert.AreEqual(modifiedTimeAfterSave, modifiedTimeAfterRead, "File should not have been rewritten");
    }

    // --- Remove ---

    [TestMethod]
    public async Task RemoveSetting_KeyDisappears()
    {
        var service = CreateService();

        await service.SaveSettingAsync("Theme", "Light");
        await service.SaveSettingAsync("Backdrop", "Mica");

        await service.RemoveSettingAsync("Theme");

        var theme = await service.ReadSettingAsync<string>("Theme");
        var backdrop = await service.ReadSettingAsync<string>("Backdrop");

        Assert.IsNull(theme);
        Assert.AreEqual("Mica", backdrop);
    }

    [TestMethod]
    public async Task ReadSetting_NonexistentKey_ReturnsDefault()
    {
        var service = CreateService();

        var result = await service.ReadSettingAsync<string>("DoesNotExist");

        Assert.IsNull(result);
    }

    // --- Test doubles ---

    private sealed class TestOptions<T>(T value) : IOptions<T> where T : class
    {
        public T Value { get; } = value;
    }
}
