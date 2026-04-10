using System.Text.Json;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Helpers;
using Hypersonic.Core.Models;

using Microsoft.Extensions.Options;

#if WINDOWS
using Windows.ApplicationModel;
using Windows.Storage;
#endif

namespace Hypersonic.Services;

public class LocalSettingsService : ILocalSettingsService
{
    private const string _defaultApplicationDataFolder = "Hypersonic/ApplicationData";
    private const string _defaultLocalSettingsFile = "LocalSettings.json";

    private readonly IFileService _fileService;
    private readonly LocalSettingsOptions _options;
    private readonly ILogger _log;

    private readonly string _localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private readonly string _applicationDataFolder;
    private readonly string _localsettingsFile;

    private IDictionary<string, object?> _settings;

    private bool _isInitialized;

    public LocalSettingsService(IFileService fileService, IOptions<LocalSettingsOptions> options, ILoggingService logService)
    {
        _fileService = fileService;
        _options = options.Value;
        _log = logService.GetLogger(nameof(LocalSettingsService));

        _applicationDataFolder = Path.Combine(_localApplicationData, _options.ApplicationDataFolder ?? _defaultApplicationDataFolder);
        _localsettingsFile = _options.LocalSettingsFile ?? _defaultLocalSettingsFile;

        _settings = new Dictionary<string, object?>();

        _log.Info("Settings initialized", new
        {
            IsMSIX = RuntimeHelper.IsMSIX,
            DataFolder = _applicationDataFolder,
            SettingsFile = _localsettingsFile,
        });
    }

    private async Task InitializeAsync()
    {
        if (!_isInitialized)
        {
            _settings = await Task.Run(() => _fileService.Read<IDictionary<string, object?>>(_applicationDataFolder, _localsettingsFile)) ?? new Dictionary<string, object?>();

            if (MigrateDoubleSerializedValues())
            {
                await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
                _log.Info("Migrated double-serialized settings to clean format");
            }

            _log.Debug("Settings file loaded", new { KeyCount = _settings.Count });
            _isInitialized = true;
        }
    }

    public async Task<T?> ReadSettingAsync<T>(string key)
    {
#if WINDOWS
        if (RuntimeHelper.IsMSIX)
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var obj))
            {
                _log.Debug("Setting read", new { Key = key });
                return await Json.ToObjectAsync<T>(obj.ToString()!);
            }
        }
        else
#endif
        {
            await InitializeAsync();

            if (_settings != null && _settings.TryGetValue(key, out var obj))
            {
                _log.Debug("Setting read", new { Key = key });

                if (obj is JsonElement je)
                {
                    return je.Deserialize<T>(Json.Options);
                }

                // Value was stored in-memory this session (raw object, not yet
                // round-tripped through the file). Serialize then deserialize to
                // return a detached copy — callers must not mutate the cached instance.
                var json = JsonSerializer.Serialize(obj, Json.Options);
                return JsonSerializer.Deserialize<T>(json, Json.Options);
            }
        }

        _log.Debug("Setting not found", new { Key = key });
        return default;
    }

    public async Task SaveSettingAsync<T>(string key, T value)
    {
#if WINDOWS
        if (RuntimeHelper.IsMSIX)
        {
            ApplicationData.Current.LocalSettings.Values[key] = await Json.StringifyAsync(value);
        }
        else
#endif
        {
            await InitializeAsync();

            _settings[key] = value;

            await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
        }

        _log.Debug("Setting saved", new { Key = key });
    }

    public async Task RemoveSettingAsync(string key)
    {
#if WINDOWS
        if (RuntimeHelper.IsMSIX)
        {
            ApplicationData.Current.LocalSettings.Values.Remove(key);
        }
        else
#endif
        {
            await InitializeAsync();

            _settings.Remove(key);

            await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
        }

        _log.Debug("Setting removed", new { Key = key });
    }

    // Keys that were affected by the double-serialization bug (pre-serialized
    // with Json.StringifyAsync before FileService.Save serialized again).
    private static readonly HashSet<string> _legacyDoubleSerializedKeys =
    [
        "AppBackgroundRequestedTheme",
        "AppRequestedBackdrop",
        "ServerAccounts",
        "ActiveAccount",
        "ServerCredential",
    ];

    private bool MigrateDoubleSerializedValues()
    {
        var migrated = false;

        foreach (var key in _legacyDoubleSerializedKeys)
        {
            if (!_settings.TryGetValue(key, out var value))
            {
                continue;
            }

            if (value is JsonElement je && je.ValueKind == JsonValueKind.String)
            {
                var raw = je.GetString();
                if (raw == null)
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    _settings[key] = doc.RootElement.Clone();
                    migrated = true;
                    _log.Debug("Migrated double-serialized setting", new { Key = key });
                }
                catch (JsonException)
                {
                    // Not valid JSON — plain string value, leave as-is.
                }
            }
        }

        return migrated;
    }
}
