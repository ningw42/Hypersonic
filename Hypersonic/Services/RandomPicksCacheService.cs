using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;

namespace Hypersonic.Services;

public sealed class RandomPicksCacheService : IRandomPicksCacheService
{
    private const string ListType = "random";
    private const int SectionSize = 20;

    private readonly ISubsonicClientService _clientService;
    private readonly ILogger _log;

    private readonly object _gate = new();
    private Task<IReadOnlyList<AlbumInfo>>? _inflight;
    private IReadOnlyList<AlbumInfo> _picks = [];
    private bool _hasLoaded;
    private bool _isLoading;

    public RandomPicksCacheService(ISubsonicClientService clientService, ILoggingService logService)
    {
        _clientService = clientService;
        _log = logService.GetLogger(nameof(RandomPicksCacheService));
    }

    public IReadOnlyList<AlbumInfo> CurrentPicks
    {
        get { lock (_gate) { return _picks; } }
    }

    public bool IsLoading
    {
        get { lock (_gate) { return _isLoading; } }
    }

    public bool HasLoaded
    {
        get { lock (_gate) { return _hasLoaded; } }
    }

    public event EventHandler? PicksChanged;
    public event EventHandler? LoadingChanged;

    public Task<IReadOnlyList<AlbumInfo>> GetOrFetchAsync()
    {
        bool raiseLoadingTrue = false;
        Task<IReadOnlyList<AlbumInfo>> task;

        lock (_gate)
        {
            if (_hasLoaded && IsTaskDone(_inflight))
            {
                return Task.FromResult(_picks);
            }

            if (!IsTaskRunning(_inflight))
            {
                _isLoading = true;
                raiseLoadingTrue = true;
                _inflight = FetchAsync();
            }
            task = _inflight!;
        }

        if (raiseLoadingTrue)
        {
            LoadingChanged?.Invoke(this, EventArgs.Empty);
        }
        return task;
    }

    public Task RefreshAsync()
    {
        bool raiseLoadingTrue = false;
        Task<IReadOnlyList<AlbumInfo>> task;

        lock (_gate)
        {
            // Coalesce: if a fetch is already in flight (initial load or prior refresh),
            // join it instead of issuing another. Per the design doc, refresh-while-in-flight
            // is a no-op for the caller.
            if (!IsTaskRunning(_inflight))
            {
                _isLoading = true;
                raiseLoadingTrue = true;
                _inflight = FetchAsync();
            }
            task = _inflight!;
        }

        if (raiseLoadingTrue)
        {
            LoadingChanged?.Invoke(this, EventArgs.Empty);
        }
        return SwallowAsync(task);
    }

    private static bool IsTaskRunning(Task? task) => task is not null && !task.IsCompleted;
    private static bool IsTaskDone(Task? task) => task is null || task.IsCompleted;

    private static async Task SwallowAsync(Task<IReadOnlyList<AlbumInfo>> task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // FetchAsync already logs and retains the previous list. Refresh never throws.
        }
    }

    private async Task<IReadOnlyList<AlbumInfo>> FetchAsync()
    {
        var client = _clientService.Client;
        IReadOnlyList<AlbumInfo> fetched = [];
        var succeeded = false;

        try
        {
            if (client is null)
            {
                _log.Warning("Random picks fetch requested but no Subsonic client is active.");
            }
            else
            {
                _log.Debug("Fetching random picks", new { Size = SectionSize });
                var response = await client.GetAlbumList2Async(ListType, size: SectionSize).ConfigureAwait(false);
                fetched = AlbumMapper.MapAll(response.AlbumList2?.Album, client);
                _log.Info("Random picks fetched", new { Count = fetched.Count });
                succeeded = true;
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to fetch random picks", exception: ex);
        }

        IReadOnlyList<AlbumInfo> result;
        bool raisePicksChanged;
        bool raiseLoadingFalse;

        lock (_gate)
        {
            // Only flip HasLoaded on success. A failed first attempt must leave the cache
            // in "not loaded" state so the next read / navigation retries instead of being
            // permanently stuck on empty picks.
            if (succeeded)
            {
                _picks = fetched;
                _hasLoaded = true;
            }
            result = _picks;
            raisePicksChanged = succeeded;
            raiseLoadingFalse = _isLoading;
            _isLoading = false;
        }

        if (raisePicksChanged)
        {
            PicksChanged?.Invoke(this, EventArgs.Empty);
        }
        if (raiseLoadingFalse)
        {
            LoadingChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }
}
