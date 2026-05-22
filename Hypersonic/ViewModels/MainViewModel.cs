using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;

using Microsoft.UI.Dispatching;

namespace Hypersonic.ViewModels;

public partial class MainViewModel : ObservableRecipient, INavigationAware
{
    private const int SectionSize = 20;
    private const int NonRandomSectionCount = 4;

    private readonly ISubsonicClientService _subsonicClientService;
    private readonly IRandomPicksCacheService _randomPicksCache;
    private readonly ILogger _log;
    private readonly DispatcherQueue? _vmDispatcher;
    private readonly Action<Action> _enqueue;

    private bool _hasLoadedOnce;
    private bool _isSubscribed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool HasAnyContent { get; set; }

    /// <summary>True while the random-picks cache is fetching (initial load or refresh).</summary>
    [ObservableProperty]
    public partial bool IsRefreshingRandomPicks { get; set; }

    public ObservableCollection<AlbumInfo> RecentlyPlayed { get; } = [];
    public ObservableCollection<AlbumInfo> RecentlyReleased { get; } = [];
    public ObservableCollection<AlbumInfo> RecentlyAdded { get; } = [];
    public ObservableCollection<AlbumInfo> MostPlayed { get; } = [];
    public ObservableCollection<AlbumInfo> RandomPicks { get; } = [];

    public bool HasRecentlyPlayed => RecentlyPlayed.Count > 0;
    public bool HasRecentlyReleased => RecentlyReleased.Count > 0;
    public bool HasRecentlyAdded => RecentlyAdded.Count > 0;
    public bool HasMostPlayed => MostPlayed.Count > 0;
    public bool HasRandomPicks => RandomPicks.Count > 0;

    public bool ShowEmptyState => _hasLoadedOnce && !IsLoading && !HasError && !HasAnyContent;

    public MainViewModel(
        ISubsonicClientService subsonicClientService,
        IRandomPicksCacheService randomPicksCache,
        ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _randomPicksCache = randomPicksCache;
        _log = logService.GetLogger(nameof(MainViewModel));
        _vmDispatcher = DispatcherQueue.GetForCurrentThread();
        _enqueue = action =>
        {
            if (_vmDispatcher is null || !_vmDispatcher.TryEnqueue(() => action()))
            {
                // Headless / test contexts: run inline.
                action();
            }
        };
    }

    /// <summary>Test hook: construct without touching DispatcherQueue.GetForCurrentThread.</summary>
    internal MainViewModel(
        ISubsonicClientService subsonicClientService,
        IRandomPicksCacheService randomPicksCache,
        ILoggingService logService,
        Action<Action> enqueue)
    {
        _subsonicClientService = subsonicClientService;
        _randomPicksCache = randomPicksCache;
        _log = logService.GetLogger(nameof(MainViewModel));
        _vmDispatcher = null;
        _enqueue = enqueue;
    }

    public async void OnNavigatedTo(object parameter)
    {
        Subscribe();
        await LoadAllAsync();
    }

    /// <summary>
    /// Test-friendly entry point that performs the same work as <see cref="OnNavigatedTo"/>
    /// (sans subscription, which tests opt into explicitly).
    /// </summary>
    internal async Task LoadAllAsync()
    {
        var loadTask = LoadSectionsAsync();
        var pickTask = PrimeRandomPicksAsync();
        await Task.WhenAll(loadTask, pickTask);
    }

    public void OnNavigatedFrom()
    {
        Unsubscribe();
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        // Per design: page-level retry must also retry a failed initial random fetch,
        // because LoadSectionsAsync no longer touches random. Refresh routes through
        // the cache's single-flight gate, so concurrent retry-plus-navigate is safe.
        var loadTask = LoadSectionsAsync();
        var refreshTask = _randomPicksCache.RefreshAsync();
        await Task.WhenAll(loadTask, refreshTask);
        // Explicitly apply the post-refresh state. In normal navigation the PicksChanged
        // subscription handles this; on retry, RetryCommand may run before navigation has
        // wired up the subscription (e.g. on the very first failed load), so this guarantees
        // the VM picks up the refreshed picks regardless.
        ApplyRandomPicks(_randomPicksCache.CurrentPicks);
    }

    [RelayCommand(CanExecute = nameof(CanRefreshRandomPicks))]
    private Task RefreshRandomPicksAsync() => _randomPicksCache.RefreshAsync();

    private bool CanRefreshRandomPicks() => !IsRefreshingRandomPicks;

    internal async Task LoadSectionsAsync()
    {
        var client = _subsonicClientService.Client;
        if (client == null)
        {
            return;
        }

        IsLoading = true;
        HasError = false;

        RecentlyPlayed.Clear();
        RecentlyReleased.Clear();
        RecentlyAdded.Clear();
        MostPlayed.Clear();

        var failureCount = 0;

        var recentlyPlayedTask = FetchSectionAsync("recent");
        var recentlyReleasedTask = FetchRecentlyReleasedAsync();
        var recentlyAddedTask = FetchSectionAsync("newest");
        var mostPlayedTask = FetchSectionAsync("frequent");

        await Task.WhenAll(recentlyPlayedTask, recentlyReleasedTask, recentlyAddedTask, mostPlayedTask);

        PopulateSection(RecentlyPlayed, recentlyPlayedTask.Result, ref failureCount);
        PopulateSection(RecentlyReleased, recentlyReleasedTask.Result, ref failureCount);
        PopulateSection(RecentlyAdded, recentlyAddedTask.Result, ref failureCount);
        PopulateSection(MostPlayed, mostPlayedTask.Result, ref failureCount);

        OnPropertyChanged(nameof(HasRecentlyPlayed));
        OnPropertyChanged(nameof(HasRecentlyReleased));
        OnPropertyChanged(nameof(HasRecentlyAdded));
        OnPropertyChanged(nameof(HasMostPlayed));

        RecomputeAggregateState(failureCount, NonRandomSectionCount);

        _hasLoadedOnce = true;
        IsLoading = false;
    }

    /// <summary>
    /// Adopt the cache's current picks if they're loaded; otherwise let the cache fetch
    /// (or join the in-flight fetch). The change event populates RandomPicks when done.
    /// </summary>
    private async Task PrimeRandomPicksAsync()
    {
        IReadOnlyList<AlbumInfo> picks;
        if (_randomPicksCache.HasLoaded && !_randomPicksCache.IsLoading)
        {
            picks = _randomPicksCache.CurrentPicks;
        }
        else
        {
            picks = await _randomPicksCache.GetOrFetchAsync();
        }

        ApplyRandomPicks(picks);
    }

    private void Subscribe()
    {
        if (_isSubscribed) return;
        _randomPicksCache.PicksChanged += OnRandomPicksChanged;
        _randomPicksCache.LoadingChanged += OnRandomPicksLoadingChanged;
        IsRefreshingRandomPicks = _randomPicksCache.IsLoading;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed) return;
        _randomPicksCache.PicksChanged -= OnRandomPicksChanged;
        _randomPicksCache.LoadingChanged -= OnRandomPicksLoadingChanged;
        _isSubscribed = false;
    }

    private void OnRandomPicksChanged(object? sender, EventArgs e)
    {
        var picks = _randomPicksCache.CurrentPicks;
        _enqueue(() => ApplyRandomPicks(picks));
    }

    private void OnRandomPicksLoadingChanged(object? sender, EventArgs e)
    {
        var loading = _randomPicksCache.IsLoading;
        _enqueue(() =>
        {
            IsRefreshingRandomPicks = loading;
            RefreshRandomPicksCommand.NotifyCanExecuteChanged();
        });
    }

    private void ApplyRandomPicks(IReadOnlyList<AlbumInfo> picks)
    {
        RandomPicks.Clear();
        foreach (var album in picks)
        {
            RandomPicks.Add(album);
        }
        OnPropertyChanged(nameof(HasRandomPicks));

        // Aggregate state must be updated whenever random changes — the ScrollViewer
        // is gated on HasAnyContent, so without this random could populate but the
        // section stay hidden when it's the only successful section.
        HasAnyContent = HasRecentlyPlayed || HasRecentlyReleased || HasRecentlyAdded || HasMostPlayed || HasRandomPicks;
        if (HasError && HasAnyContent)
        {
            HasError = false;
        }
    }

    private void RecomputeAggregateState(int failureCount, int taskCount)
    {
        HasAnyContent = HasRecentlyPlayed || HasRecentlyReleased || HasRecentlyAdded || HasMostPlayed || HasRandomPicks;
        HasError = !HasAnyContent && failureCount == taskCount;
    }

    private static void PopulateSection(ObservableCollection<AlbumInfo> target, List<AlbumInfo>? source, ref int failureCount)
    {
        if (source == null)
        {
            failureCount++;
            return;
        }

        foreach (var album in source)
        {
            target.Add(album);
        }
    }

    private async Task<List<AlbumInfo>?> FetchSectionAsync(string type)
    {
        var client = _subsonicClientService.Client;
        if (client == null)
        {
            return null;
        }

        try
        {
            _log.Debug("Fetching home section", new { Type = type });
            var response = await client.GetAlbumList2Async(type, size: SectionSize);
            var albums = AlbumMapper.MapAll(response.AlbumList2?.Album, client);
            _log.Info("Home section fetched", new { Type = type, Count = albums.Count });
            return albums;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to fetch home section '{type}'", exception: ex);
            return null;
        }
    }

    // Subsonic API: "If fromYear > toYear a reverse chronological list is returned."
    // Setting fromYear=currentYear, toYear=0 returns all albums ordered newest-first by year tag.
    // See: https://opensubsonic.netlify.app/docs/endpoints/getalbumlist2/
    private async Task<List<AlbumInfo>?> FetchRecentlyReleasedAsync()
    {
        var client = _subsonicClientService.Client;
        if (client == null)
        {
            return null;
        }

        try
        {
            _log.Debug("Fetching recently released");
            var response = await client.GetAlbumList2Async("byYear", size: SectionSize, fromYear: DateTime.Now.Year, toYear: 0);
            var albums = AlbumMapper.MapAll(response.AlbumList2?.Album, client);
            _log.Info("Recently released fetched", new { Count = albums.Count });
            return albums;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to fetch recently released", exception: ex);
            return null;
        }
    }
}
