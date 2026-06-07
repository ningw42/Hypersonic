using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Collections;
using Hypersonic.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;
using Hypersonic.Models;

namespace Hypersonic.ViewModels;

public partial class TracksViewModel : ObservableRecipient, INavigationAware
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly IPlaybackService _playbackService;
    private readonly ILogger _log;
    internal List<SongInfo> _allSongs = [];
    internal NowPlayingTracker? _nowPlayingTracker;
    private long _loadGeneration;
    private bool _navigatedAway;
    private bool _lastLoadFailed;
    private int _offset;
    private string _searchQuery = "";
    private CancellationTokenSource? _searchCts;
    private bool _suppressSortOnDirectionChange;

    private static readonly int[] DefaultDirectionDescending = [0, 0, 0, 0, 0, 1];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    public partial bool HasLoadedOnce { get; set; }

    public bool IsEmpty => HasLoadedOnce && _allSongs.Count == 0 && string.IsNullOrEmpty(ErrorMessage);

    public string EmptyMessage => string.IsNullOrEmpty(_searchQuery) ? "No tracks found" : "No results";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortOrderSupported))]
    [NotifyPropertyChangedFor(nameof(SortOrderTooltip))]
    public partial int SelectedSortIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortOrderGlyph))]
    [NotifyPropertyChangedFor(nameof(SortOrderTooltip))]
    public partial bool IsSortDescending { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public string SortOrderGlyph => IsSortDescending ? "" : "";

    public bool IsSortOrderSupported => SelectedSortIndex > 0;

    public string SortOrderTooltip => IsSortDescending ? "Sort descending" : "Sort ascending";

    public List<string> SortOptions { get; } =
    [
        "Default",
        "Title",
        "Artist",
        "Album",
        "Duration",
        "Year",
    ];

    public IncrementalSongCollection Tracks { get; }

    public TracksViewModel(ISubsonicClientService subsonicClientService, IPlaybackService playbackService, ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _playbackService = playbackService;
        _log = logService.GetLogger(nameof(TracksViewModel));
        Tracks = new IncrementalSongCollection(LoadMoreSongsAsync);
        Tracks.OnBatchLoaded = OnListViewBatchLoaded;
    }

    private void OnListViewBatchLoaded()
    {
        if (SelectedSortIndex > 0)
        {
            ApplySortingInternal();
        }
    }

    public async void OnNavigatedTo(object parameter)
    {
        _navigatedAway = false;
        ErrorMessage = null;

        if (Tracks.Count == 0 && !_lastLoadFailed)
        {
            await LoadFirstBatchAsync();
        }
        else if (_lastLoadFailed)
        {
            await RetryFailedLoadAsync();
        }

        if (_allSongs.Count > 0)
        {
            _nowPlayingTracker?.Unsubscribe();
            _nowPlayingTracker = new NowPlayingTracker(_playbackService, () => _allSongs);
            _nowPlayingTracker.Subscribe();
        }
    }

    public void OnNavigatedFrom()
    {
        _loadGeneration++;
        _navigatedAway = true;
        _searchCts?.Cancel();
        _nowPlayingTracker?.Unsubscribe();
    }

    partial void OnSelectedSortIndexChanged(int oldValue, int newValue)
    {
        if (newValue == 0)
        {
            return;
        }

        _suppressSortOnDirectionChange = true;
        IsSortDescending = DefaultDirectionDescending[newValue] == 1;
        _suppressSortOnDirectionChange = false;
        ApplySorting();
    }

    partial void OnIsSortDescendingChanged(bool value)
    {
        if (SelectedSortIndex > 0 && !_suppressSortOnDirectionChange)
        {
            ApplySorting();
        }
    }

    [RelayCommand]
    private void ToggleSortOrder()
    {
        IsSortDescending = !IsSortDescending;
    }

    public async Task OnSearchTextChangedAsync(string text)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            await Task.Delay(300, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        var trimmed = text.Trim();
        await ExecuteSearchAsync(trimmed);
    }

    private async Task ExecuteSearchAsync(string query)
    {
        _loadGeneration++;
        ErrorMessage = null;
        _searchQuery = query;
        _offset = 0;
        _allSongs.Clear();
        Tracks.Reset();
        _lastLoadFailed = false;

        SelectedSortIndex = 0;

        await LoadFirstBatchAsync();
    }

    private async Task LoadFirstBatchAsync()
    {
        IsLoading = true;
        try
        {
            var items = await LoadMoreSongsAsync((uint)Tracks.PageSize);
            if (items != null)
            {
                if (items.Count > 0)
                {
                    Tracks.AddExternalBatch(items);

                    if (SelectedSortIndex > 0)
                    {
                        ApplySortingInternal();
                    }
                }

                HasLoadedOnce = true;
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(EmptyMessage));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RetryFailedLoadAsync()
    {
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var items = await LoadMoreSongsAsync((uint)Tracks.PageSize);
            if (items != null && items.Count > 0)
            {
                Tracks.AddExternalBatch(items);

                if (SelectedSortIndex > 0)
                {
                    ApplySortingInternal();
                }
            }

            if (items != null)
            {
                HasLoadedOnce = true;
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(EmptyMessage));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal async Task<IList<SongInfo>?> LoadMoreSongsAsync(uint count)
    {
        var capturedGeneration = _loadGeneration;

        var client = _subsonicClientService.Client;
        if (client == null)
        {
            return [];
        }

        try
        {
            var response = await client.Search3Async(
                _searchQuery,
                songCount: (int)count,
                songOffset: _offset,
                artistCount: 0,
                albumCount: 0);

            if (_loadGeneration != capturedGeneration || _navigatedAway)
            {
                _log.Debug("Discarding stale search result", new { capturedGeneration, _loadGeneration });
                return null;
            }

            var songs = response.SearchResult3?.Song;
            if (songs == null || songs.Length == 0)
            {
                _lastLoadFailed = false;
                return [];
            }

            var items = new List<SongInfo>(songs.Length);
            var startPosition = _allSongs.Count + 1;

            foreach (var song in songs)
            {
                var info = SongMapper.MapFromChild(song, client);
                info.PositionNumber = startPosition++;
                items.Add(info);
            }

            _offset += songs.Length;
            _allSongs.AddRange(items);

            _nowPlayingTracker?.Refresh(force: true);

            _lastLoadFailed = false;
            return items;
        }
        catch (Exception ex)
        {
            if (_loadGeneration != capturedGeneration || _navigatedAway)
            {
                _log.Debug("Discarding stale search exception", new { capturedGeneration, _loadGeneration, Error = ex.Message });
                return null;
            }

            if (_offset == 0 && string.IsNullOrEmpty(_searchQuery))
            {
                ErrorMessage = "Failed to load tracks. Your server may not support browsing all tracks.";
            }
            else
            {
                ErrorMessage = "Failed to load tracks.";
            }

            _lastLoadFailed = true;
            _log.Error("Failed to load tracks", new { _offset, _searchQuery }, ex);
            return [];
        }
    }

    private void ApplySorting()
    {
        if (_allSongs.Count == 0)
        {
            return;
        }

        ApplySortingInternal();
    }

    private void ApplySortingInternal()
    {
        var sorted = SelectedSortIndex switch
        {
            1 => IsSortDescending
                ? _allSongs.OrderByDescending(s => s.Title, StringComparer.OrdinalIgnoreCase)
                : _allSongs.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
            2 => IsSortDescending
                ? _allSongs.OrderByDescending(s => s.Artist, StringComparer.OrdinalIgnoreCase)
                : _allSongs.OrderBy(s => s.Artist, StringComparer.OrdinalIgnoreCase),
            3 => IsSortDescending
                ? _allSongs.OrderByDescending(s => s.Album, StringComparer.OrdinalIgnoreCase)
                : _allSongs.OrderBy(s => s.Album, StringComparer.OrdinalIgnoreCase),
            4 => IsSortDescending
                ? _allSongs.OrderByDescending(s => s.Duration)
                : _allSongs.OrderBy(s => s.Duration),
            5 => IsSortDescending
                ? _allSongs.OrderByDescending(s => s.Year)
                : _allSongs.OrderBy(s => s.Year),
            _ => null,
        };

        if (sorted == null)
        {
            return;
        }

        _allSongs = sorted.ToList();

        for (var i = 0; i < _allSongs.Count; i++)
        {
            _allSongs[i].PositionNumber = i + 1;
        }

        Tracks.ReplaceAll(_allSongs);
        _nowPlayingTracker?.Refresh(force: true);
    }

    [RelayCommand]
    private void PlayFromTrack(SongInfo? song)
    {
        if (song == null || _allSongs.Count == 0)
        {
            return;
        }

        var tracks = BuildPlaybackTracks();
        var index = _allSongs.IndexOf(song);
        _log.Info("Playing tracks from track", new { TrackId = song.Id, song.Title, Index = index });
        _playbackService.PlayQueue(tracks, index >= 0 ? index : 0);
    }

    [RelayCommand]
    private async Task ToggleStarAsync(SongInfo? song)
    {
        if (song == null)
        {
            return;
        }

        var client = _subsonicClientService.Client;
        if (client == null)
        {
            return;
        }

        var wasStarred = song.IsStarred;
        song.IsStarred = !wasStarred;

        try
        {
            if (wasStarred)
            {
                await client.UnstarAsync(id: song.Id);
            }
            else
            {
                await client.StarAsync(id: song.Id);
            }
        }
        catch (Exception ex)
        {
            song.IsStarred = wasStarred;
            _log.Warning("Failed to toggle star", new { SongId = song.Id, wasStarred }, ex);
        }
    }

    internal List<PlaybackTrack> BuildPlaybackTracks()
    {
        return _allSongs.Select(s =>
        {
            var (artists, hasStructured) = SongArtistMapper.MapArtists(
                s.Artists.Select(a => (a.Name, (string?)a.Id)),
                s.Artist,
                s.ArtistId);

            return new PlaybackTrack
            {
                Id = s.Id,
                Title = s.Title,
                Artist = s.Artist,
                ArtistId = s.ArtistId,
                DisplayArtist = s.DisplayArtist,
                Artists = artists,
                HasStructuredArtists = hasStructured,
                FlatArtistText = DisplayArtistRenderer.BuildFlatText(s.DisplayArtist, artists, hasStructured),
                Album = s.Album,
                AlbumId = s.AlbumId,
                Duration = s.Duration,
                TrackNumber = s.TrackNumber,
                DiscNumber = s.DiscNumber,
                CoverImage = s.CoverImage,
                StreamUri = s.StreamUri,
                Suffix = s.Suffix,
                BitRate = s.BitRate,
                BitDepth = s.BitDepth,
                SamplingRate = s.SamplingRate,
                IsStarred = s.IsStarred,
            };
        }).ToList();
    }
}
