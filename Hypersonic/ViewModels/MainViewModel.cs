using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;

namespace Hypersonic.ViewModels;

public partial class MainViewModel : ObservableRecipient, INavigationAware
{
    private const int SectionSize = 20;

    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ILogger _log;
    private bool _hasLoadedOnce;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool HasAnyContent { get; set; }

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

    public MainViewModel(ISubsonicClientService subsonicClientService, ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _log = logService.GetLogger(nameof(MainViewModel));
    }

    public async void OnNavigatedTo(object parameter)
    {
        await LoadSectionsAsync();
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        await LoadSectionsAsync();
    }

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
        RandomPicks.Clear();

        var failureCount = 0;

        var recentlyPlayedTask = FetchSectionAsync("recent");
        var recentlyReleasedTask = FetchRecentlyReleasedAsync();
        var recentlyAddedTask = FetchSectionAsync("newest");
        var mostPlayedTask = FetchSectionAsync("frequent");
        var randomPicksTask = FetchSectionAsync("random");

        await Task.WhenAll(recentlyPlayedTask, recentlyReleasedTask, recentlyAddedTask, mostPlayedTask, randomPicksTask);

        PopulateSection(RecentlyPlayed, recentlyPlayedTask.Result, ref failureCount);
        PopulateSection(RecentlyReleased, recentlyReleasedTask.Result, ref failureCount);
        PopulateSection(RecentlyAdded, recentlyAddedTask.Result, ref failureCount);
        PopulateSection(MostPlayed, mostPlayedTask.Result, ref failureCount);
        PopulateSection(RandomPicks, randomPicksTask.Result, ref failureCount);

        OnPropertyChanged(nameof(HasRecentlyPlayed));
        OnPropertyChanged(nameof(HasRecentlyReleased));
        OnPropertyChanged(nameof(HasRecentlyAdded));
        OnPropertyChanged(nameof(HasMostPlayed));
        OnPropertyChanged(nameof(HasRandomPicks));

        HasAnyContent = HasRecentlyPlayed || HasRecentlyReleased || HasRecentlyAdded || HasMostPlayed || HasRandomPicks;
        HasError = !HasAnyContent && failureCount == 5;

        _hasLoadedOnce = true;
        IsLoading = false;
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
