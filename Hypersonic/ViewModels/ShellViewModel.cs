using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;
using Hypersonic.Views;

using Microsoft.UI.Xaml.Navigation;

namespace Hypersonic.ViewModels;

public partial class ShellViewModel : ObservableRecipient
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly IPlaybackService _playbackService;
    private readonly IAppStateService _appStateService;
    private readonly IFavoritesService _favorites;
    private readonly ILogger _log;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    public partial bool IsBackEnabled { get; set; }

    [ObservableProperty]
    public partial object? Selected { get; set; }

    [ObservableProperty]
    public partial bool IsSearchResultsVisible { get; set; }

    [ObservableProperty]
    public partial bool HasArtistResults { get; set; }

    [ObservableProperty]
    public partial bool HasAlbumResults { get; set; }

    [ObservableProperty]
    public partial bool HasSongResults { get; set; }

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
        IAppStateService appStateService,
        IFavoritesService favoritesService,
        ILoggingService logService)
    {
        NavigationService = navigationService;
        NavigationService.Navigated += OnNavigated;
        NavigationViewService = navigationViewService;
        _subsonicClientService = subsonicClientService;
        _playbackService = playbackService;
        _appStateService = appStateService;
        _favorites = favoritesService;
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
                        Image = ImageReference.SubsonicCoverArt(a.CoverArt),
                    });
                }
            }

            var albums = new List<SearchSuggestionItem>();
            if (result.Album?.Length > 0)
            {
                foreach (var album in result.Album)
                {
                    var (shortName, effectiveVersion) = AlbumMapper.SplitAlbumVersion(album.Name, album.Version);
                    var secondaryText = album.Artist ?? string.Empty;
                    if (!string.IsNullOrEmpty(effectiveVersion))
                    {
                        secondaryText = $"{secondaryText} · {effectiveVersion}";
                    }

                    albums.Add(new SearchSuggestionItem
                    {
                        Type = SearchResultType.Album,
                        Id = album.Id,
                        PrimaryText = shortName,
                        SecondaryText = secondaryText,
                        Image = ImageReference.SubsonicCoverArt(album.CoverArt),
                    });
                }
            }

            var songs = new List<SearchSuggestionItem>();
            if (result.Song?.Length > 0)
            {
                foreach (var song in result.Song)
                {
                    var (songArtists, songHasStructured) = SongArtistMapper.MapArtists(
                        song.Artists?.Select(a => (a.Name, (string?)a.Id)),
                        song.Artist,
                        song.ArtistId);

                    songs.Add(new SearchSuggestionItem
                    {
                        Type = SearchResultType.Song,
                        Id = song.Id,
                        PrimaryText = song.Title ?? string.Empty,
                        SecondaryText = $"{song.Artist} \u00b7 {song.Album}",
                        Image = ImageReference.SubsonicCoverArt(song.CoverArt),
                        Artist = song.Artist,
                        ArtistId = song.ArtistId,
                        DisplayArtist = song.DisplayArtist,
                        Artists = songArtists,
                        HasStructuredArtists = songHasStructured,
                        Album = song.Album,
                        AlbumId = song.AlbumId,
                        Duration = song.Duration,
                        StreamUri = client.GetStreamUri(song.Id),
                        Suffix = song.Suffix,
                        BitRate = song.BitRate,
                        BitDepth = song.BitDepth,
                        SamplingRate = song.SamplingRate,
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
                    DisplayArtist = item.DisplayArtist,
                    Artists = item.Artists,
                    HasStructuredArtists = item.HasStructuredArtists,
                    FlatArtistText = DisplayArtistRenderer.BuildFlatText(item.DisplayArtist, item.Artists, item.HasStructuredArtists),
                    Album = item.Album ?? string.Empty,
                    AlbumId = item.AlbumId,
                    Duration = item.Duration,
                    CoverImage = item.Image,
                    StreamUri = item.StreamUri,
                    Suffix = item.Suffix,
                    BitRate = item.BitRate,
                    BitDepth = item.BitDepth,
                    SamplingRate = item.SamplingRate,
                    // Search suggestions carry no star field; seed from the store so a
                    // search-played track that's already favorited shows starred immediately
                    // (no future poll delta would otherwise ever fire for it).
                    IsStarred = _favorites.IsStarred(item.Id),
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
        }
        else
        {
            var selectedItem = NavigationViewService.GetSelectedItem(e.SourcePageType);
            if (selectedItem != null)
            {
                Selected = selectedItem;
            }
        }

        var vmKey = LastPagePersistencePolicy.GetPersistableViewModelKey(e.SourcePageType);
        if (vmKey != null)
        {
            _appStateService.State.Navigation.LastPage = vmKey;
            _ = _appStateService.SaveNavigationAsync();
        }
    }
}
