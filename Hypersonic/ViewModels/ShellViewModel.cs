using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Hypersonic.Contracts.Services;
using Hypersonic.Models;
using Hypersonic.Views;

using Microsoft.UI.Xaml.Navigation;

namespace Hypersonic.ViewModels;

public partial class ShellViewModel : ObservableRecipient
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly IPlaybackService _playbackService;
    private readonly ILogger _log;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private bool isBackEnabled;

    [ObservableProperty]
    private object? selected;

    [ObservableProperty]
    private bool isSearchResultsVisible;

    [ObservableProperty]
    private bool hasArtistResults;

    [ObservableProperty]
    private bool hasAlbumResults;

    [ObservableProperty]
    private bool hasSongResults;

    public ObservableCollection<SearchSuggestionItem> ArtistResults { get; } = [];
    public ObservableCollection<SearchSuggestionItem> AlbumResults { get; } = [];
    public ObservableCollection<SearchSuggestionItem> SongResults { get; } = [];

    public INavigationService NavigationService
    {
        get;
    }

    public INavigationViewService NavigationViewService
    {
        get;
    }

    public ShellViewModel(
        INavigationService navigationService,
        INavigationViewService navigationViewService,
        ISubsonicClientService subsonicClientService,
        IPlaybackService playbackService,
        ILoggingService logService)
    {
        NavigationService = navigationService;
        NavigationService.Navigated += OnNavigated;
        NavigationViewService = navigationViewService;
        _subsonicClientService = subsonicClientService;
        _playbackService = playbackService;
        _log = logService.GetLogger(nameof(ShellViewModel));
    }

    public async Task PerformSearchAsync(string query)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            await Task.Delay(300, token);

            var client = _subsonicClientService.Client;
            if (client == null)
            {
                return;
            }

            var response = await client.Search3Async(
                query,
                artistCount: 5,
                albumCount: 5,
                songCount: 5);

            if (token.IsCancellationRequested)
            {
                return;
            }

            var result = response.SearchResult3;

            var artists = new List<SearchSuggestionItem>();
            if (result.Artists?.Length > 0)
            {
                foreach (var a in result.Artists)
                {
                    artists.Add(new SearchSuggestionItem
                    {
                        Type = SearchResultType.Artist,
                        Id = a.Id,
                        PrimaryText = a.Name ?? string.Empty,
                        SecondaryText = a.AlbumCount == 1 ? "1 album" : $"{a.AlbumCount} albums",
                        ImageUrl = !string.IsNullOrEmpty(a.CoverArt)
                            ? client.GetCoverArtUri(a.CoverArt, "100").ToString()
                            : null,
                    });
                }
            }

            var albums = new List<SearchSuggestionItem>();
            if (result.Album?.Length > 0)
            {
                foreach (var album in result.Album)
                {
                    albums.Add(new SearchSuggestionItem
                    {
                        Type = SearchResultType.Album,
                        Id = album.Id,
                        PrimaryText = album.Name ?? string.Empty,
                        SecondaryText = album.Artist ?? string.Empty,
                        ImageUrl = !string.IsNullOrEmpty(album.CoverArt)
                            ? client.GetCoverArtUri(album.CoverArt, "100").ToString()
                            : null,
                    });
                }
            }

            var songs = new List<SearchSuggestionItem>();
            if (result.Song?.Length > 0)
            {
                foreach (var song in result.Song)
                {
                    songs.Add(new SearchSuggestionItem
                    {
                        Type = SearchResultType.Song,
                        Id = song.Id,
                        PrimaryText = song.Title ?? string.Empty,
                        SecondaryText = $"{song.Artist} \u00b7 {song.Album}",
                        ImageUrl = !string.IsNullOrEmpty(song.CoverArt)
                            ? client.GetCoverArtUri(song.CoverArt, "100").ToString()
                            : null,
                        Artist = song.Artist,
                        ArtistId = song.ArtistId,
                        Album = song.Album,
                        AlbumId = song.AlbumId,
                        Duration = song.Duration,
                        StreamUri = client.GetStreamUri(song.Id),
                        CoverArtUri = !string.IsNullOrEmpty(song.CoverArt)
                            ? client.GetCoverArtUri(song.CoverArt, "300")
                            : null,
                    });
                }
            }

            ArtistResults.Clear();
            foreach (var item in artists)
            {
                ArtistResults.Add(item);
            }

            AlbumResults.Clear();
            foreach (var item in albums)
            {
                AlbumResults.Add(item);
            }

            SongResults.Clear();
            foreach (var item in songs)
            {
                SongResults.Add(item);
            }

            HasArtistResults = artists.Count > 0;
            HasAlbumResults = albums.Count > 0;
            HasSongResults = songs.Count > 0;
            IsSearchResultsVisible = HasArtistResults || HasAlbumResults || HasSongResults;

            _log.Debug("Search completed", new { Query = query, Artists = artists.Count, Albums = albums.Count, Songs = songs.Count });
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warning("Search failed", new { Query = query }, ex);
        }
    }

    public void ClearSearch()
    {
        ArtistResults.Clear();
        AlbumResults.Clear();
        SongResults.Clear();
        HasArtistResults = false;
        HasAlbumResults = false;
        HasSongResults = false;
        IsSearchResultsVisible = false;
    }

    public void HandleSuggestionChosen(SearchSuggestionItem item)
    {
        switch (item.Type)
        {
            case SearchResultType.Artist:
                NavigationService.NavigateTo(typeof(ArtistDetailViewModel).FullName!, item.Id);
                break;
            case SearchResultType.Album:
                NavigationService.NavigateTo(typeof(AlbumDetailViewModel).FullName!, item.Id);
                break;
            case SearchResultType.Song:
                var track = new PlaybackTrack
                {
                    Id = item.Id,
                    Title = item.PrimaryText,
                    Artist = item.Artist ?? string.Empty,
                    ArtistId = item.ArtistId,
                    Album = item.Album ?? string.Empty,
                    AlbumId = item.AlbumId,
                    Duration = item.Duration,
                    StreamUri = item.StreamUri,
                    CoverArtUri = item.CoverArtUri,
                };
                _playbackService.PlayQueue([track], 0);
                break;
        }

        ClearSearch();
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        IsBackEnabled = NavigationService.CanGoBack;

        if (e.SourcePageType == typeof(SettingsPage))
        {
            Selected = NavigationViewService.SettingsItem;
            return;
        }

        var selectedItem = NavigationViewService.GetSelectedItem(e.SourcePageType);
        if (selectedItem != null)
        {
            Selected = selectedItem;
        }
    }
}
