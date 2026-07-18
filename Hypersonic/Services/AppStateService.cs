using System.Text.Json;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Helpers;
using Hypersonic.Core.Models;

using Windows.Storage;

namespace Hypersonic.Services;

public class AppStateService : IAppStateService
{
    private const string StateFileName = "AppState.json";

    private readonly string _filePath;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AppState State { get; private set; } = new();

    public AppStateService(ILoggingService logService)
    {
        _log = logService.GetLogger(nameof(AppStateService));

        var baseDir = RuntimeHelper.IsMSIX
            ? ApplicationData.Current.LocalFolder.Path
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hypersonic", "ApplicationData");

        _filePath = Path.Combine(baseDir, StateFileName);
        _log.Debug("AppState file path", new { Path = _filePath });
    }

    public async Task InitializeAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await Task.Run(() => File.ReadAllText(_filePath));
                var state = JsonSerializer.Deserialize<AppState>(json, Json.Options);
                if (state != null)
                {
                    State = state;
                    _log.Info("AppState loaded from file");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to load AppState, using defaults", exception: ex);
        }

        State = new AppState();
    }

    public Task SaveAsync() => WriteStateAsync();

    public Task SavePlaybackAsync() => WriteStateAsync();

    public Task SaveNavigationAsync() => WriteStateAsync();

    public Task SaveGeneralAsync() => WriteStateAsync();

    /// <summary>
    /// Synchronous write for use during window close, where async + Task.Run
    /// would deadlock or race against process exit. Acquires the write lock
    /// so no in-flight async write can overwrite the final state.
    /// </summary>
    public void SaveSync()
    {
        _writeLock.Wait();
        try
        {
            WriteToFile();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteStateAsync()
    {
        // Snapshot the JSON on the caller's thread so the serialization
        // always captures the latest State, even if a previous write is
        // still in flight.
        var json = JsonSerializer.Serialize(State, Json.Options);

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            await Task.Run(() => File.WriteAllText(_filePath, json, System.Text.Encoding.UTF8)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save AppState", exception: ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void WriteToFile()
    {
        try
        {
            EnsureDirectory();
            var json = JsonSerializer.Serialize(State, Json.Options);
            File.WriteAllText(_filePath, json, System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save AppState (sync)", exception: ex);
        }
    }

    private void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
