using Hypersonic.Contracts.Services;
using Hypersonic.Helpers;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

using Windows.ApplicationModel;
using Windows.Storage;

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

    private IDictionary<string, object> _settings;

    private bool _isInitialized;

    public LocalSettingsService(IFileService fileService, IOptions<LocalSettingsOptions> options, ILoggingService logService)
    {
        _fileService = fileService;
        _options = options.Value;
        _log = logService.GetLogger(nameof(LocalSettingsService));

        _applicationDataFolder = Path.Combine(_localApplicationData, _options.ApplicationDataFolder ?? _defaultApplicationDataFolder);
        _localsettingsFile = _options.LocalSettingsFile ?? _defaultLocalSettingsFile;

        _settings = new Dictionary<string, object>();

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
            _settings = await Task.Run(() => _fileService.Read<IDictionary<string, object>>(_applicationDataFolder, _localsettingsFile)) ?? new Dictionary<string, object>();

            _log.Debug("Settings file loaded", new { KeyCount = _settings.Count });
            _isInitialized = true;
        }
    }

    public async Task<T?> ReadSettingAsync<T>(string key)
    {
        if (RuntimeHelper.IsMSIX)
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var obj))
            {
                _log.Debug("Setting read", new { Key = key });
                return await Json.ToObjectAsync<T>(obj.ToString()!);
            }
        }
        else
        {
            await InitializeAsync();

            if (_settings != null && _settings.TryGetValue(key, out var obj))
            {
                _log.Debug("Setting read", new { Key = key });
                return await Json.ToObjectAsync<T>(obj.ToString()!);
            }
        }

        _log.Debug("Setting not found", new { Key = key });
        return default;
    }

    public async Task SaveSettingAsync<T>(string key, T value)
    {
        if (RuntimeHelper.IsMSIX)
        {
            ApplicationData.Current.LocalSettings.Values[key] = await Json.StringifyAsync(value);
        }
        else
        {
            await InitializeAsync();

            _settings[key] = await Json.StringifyAsync(value);

            await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
        }

        _log.Debug("Setting saved", new { Key = key });
    }

    public async Task RemoveSettingAsync(string key)
    {
        if (RuntimeHelper.IsMSIX)
        {
            ApplicationData.Current.LocalSettings.Values.Remove(key);
        }
        else
        {
            await InitializeAsync();

            _settings.Remove(key);

            await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
        }

        _log.Debug("Setting removed", new { Key = key });
    }
}
