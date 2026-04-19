using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Core.Models;

namespace Hypersonic.ViewModels;

public partial class ArtistDetailViewModel : ObservableRecipient, INavigationAware
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly INavigationService _navigationService;
    private readonly ILogger _log;
    private List<AlbumInfo> _allAlbums = [];

    [ObservableProperty]
    public partial string ArtistName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ArtistImageUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumCountText))]
    public partial int AlbumCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSongCountText))]
    public partial int TotalSongCount { get; set; }

    [ObservableProperty]
    public partial string? Biography { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BiographyMaxLines))]
    [NotifyPropertyChangedFor(nameof(BiographyToggleText))]
    public partial bool IsBiographyExpanded { get; set; }

    [ObservableProperty]
    public partial bool HasBiography { get; set; }

    [ObservableProperty]
    public partial bool HasSimilarArtists { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortOrderTooltip))]
    public partial int SelectedSortIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortOrderGlyph))]
    [NotifyPropertyChangedFor(nameof(SortOrderTooltip))]
    public partial bool IsSortDescending { get; set; } = true;

    public string SortOrderGlyph => IsSortDescending ? "\uE74B" : "\uE74A";

    public string SortOrderTooltip => IsSortDescending ? "Sort descending" : "Sort ascending";

    public string AlbumCountText => AlbumCount == 1 ? "1 album" : $"{AlbumCount} albums";

    public string TotalSongCountText => TotalSongCount == 1 ? "1 song" : $"{TotalSongCount} songs";

    public int BiographyMaxLines => IsBiographyExpanded ? 999 : 3;

    public string BiographyToggleText => IsBiographyExpanded ? "Show less" : "Show more";

    public string? CurrentArtistId { get; private set; }

    public ObservableCollection<AlbumInfo> Albums { get; } = [];

    public ObservableCollection<ArtistInfo> SimilarArtists { get; } = [];

    public List<string> SortOptions { get; } =
    [
        "Release Date",
        "Name",
    ];

    public ArtistDetailViewModel(ISubsonicClientService subsonicClientService, INavigationService navigationService, ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _navigationService = navigationService;
        _log = logService.GetLogger(nameof(ArtistDetailViewModel));
    }

    public async void OnNavigatedTo(object parameter)
    {
        if (parameter is string artistId)
        {
            await LoadArtistAsync(artistId);
        }
    }

    public void OnNavigatedFrom()
    {
    }

    partial void OnSelectedSortIndexChanged(int value)
    {
        ApplySorting();
    }

    partial void OnIsSortDescendingChanged(bool value)
    {
        ApplySorting();
    }

    [RelayCommand]
    private void ToggleSortOrder()
    {
        IsSortDescending = !IsSortDescending;
    }

    [RelayCommand]
    private void ToggleBiography()
    {
        IsBiographyExpanded = !IsBiographyExpanded;
    }

    [RelayCommand]
    private void NavigateToAlbum(AlbumInfo? album)
    {
        if (album == null)
        {
            return;
        }

        _navigationService.NavigateTo(typeof(AlbumDetailViewModel).FullName!, album.Id);
    }

    [RelayCommand]
    private void NavigateToArtist(ArtistInfo? artist)
    {
        if (artist == null)
        {
            return;
        }

        _navigationService.NavigateTo(typeof(ArtistDetailViewModel).FullName!, artist.Id);
    }

    internal async Task LoadArtistAsync(string artistId)
    {
        var client = _subsonicClientService.Client;
        if (client == null)
        {
            return;
        }

        CurrentArtistId = artistId;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var artistTask = client.GetArtistAsync(artistId);
            var infoTask = client.GetArtistInfo2Async(artistId);

            await Task.WhenAll(artistTask, infoTask);

            var artist = artistTask.Result.Artist;
            if (artist == null)
            {
                ErrorMessage = "Artist not found.";
                return;
            }

            _log.Info("Artist fetched", new { ArtistId = artistId, artist.Name, artist.AlbumCount });

            ArtistName = artist.Name ?? string.Empty;
            AlbumCount = artist.AlbumCount;

            var albums = artist.Album ?? [];
            _allAlbums = albums.Select(a =>
            {
                var (artists, hasStructured) = SongArtistMapper.MapArtists(
                    a.Artists?.Select(x => (x.Name, (string?)x.Id)),
                    a.Artist, a.ArtistId);

                return new AlbumInfo
                {
                    Id = a.Id,
                    Name = a.Name ?? string.Empty,
                    Artist = a.DisplayArtist ?? a.Artist ?? string.Empty,
                    ArtistId = a.ArtistId,
                    DisplayArtist = a.DisplayArtist,
                    Artists = artists,
                    HasStructuredArtists = hasStructured,
                    Year = a.Year,
                    CoverArt = a.CoverArt,
                    CoverArtUrl = !string.IsNullOrEmpty(a.CoverArt)
                        ? client.GetCoverArtUri(a.CoverArt, "300").ToString()
                        : null,
                    SongCount = a.SongCount,
                    Duration = TimeSpan.FromSeconds(a.Duration),
                };
            }).ToList();

            TotalSongCount = _allAlbums.Sum(a => a.SongCount ?? 0);

            // Artist image fallback chain
            ArtistImageUrl = !string.IsNullOrEmpty(artist.ArtistImageUrl)
                ? artist.ArtistImageUrl
                : !string.IsNullOrEmpty(artist.CoverArt)
                    ? client.GetCoverArtUri(artist.CoverArt, "600").ToString()
                    : null;

            // Process artist info (biography, similar artists, image fallback)
            try
            {
                var info = infoTask.Result.ArtistInfo2;
                if (info != null)
                {
                    if (!string.IsNullOrWhiteSpace(info.Biography))
                    {
                        Biography = Regex.Replace(info.Biography, "<.*?>", string.Empty).Trim();
                        HasBiography = !string.IsNullOrWhiteSpace(Biography);
                    }

                    // Image fallback from info2
                    if (string.IsNullOrEmpty(ArtistImageUrl))
                    {
                        ArtistImageUrl = info.LargeImageUrl ?? info.MediumImageUrl;
                    }

                    if (info.SimilarArtist != null && info.SimilarArtist.Length > 0)
                    {
                        foreach (var similar in info.SimilarArtist)
                        {
                            SimilarArtists.Add(new ArtistInfo
                            {
                                Id = similar.Id,
                                Name = similar.Name ?? string.Empty,
                                AlbumCount = similar.AlbumCount,
                                CoverArt = similar.CoverArt,
                                CoverArtUrl = !string.IsNullOrEmpty(similar.CoverArt)
                                    ? client.GetCoverArtUri(similar.CoverArt, "300").ToString()
                                    : null,
                            });
                        }

                        HasSimilarArtists = true;
                    }
                }
            }
            catch (Exception)
            {
                // GetArtistInfo2 may not be supported; page still works without it
                _log.Debug("GetArtistInfo2 unavailable");
            }

            ApplySorting();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _log.Error("Failed to load artist", new { ArtistId = artistId }, ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplySorting()
    {
        if (_allAlbums.Count == 0)
        {
            return;
        }

        IEnumerable<AlbumInfo> sorted = SelectedSortIndex switch
        {
            1 => IsSortDescending
                ? _allAlbums.OrderByDescending(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                : _allAlbums.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => IsSortDescending
                ? _allAlbums.OrderByDescending(a => a.Year)
                : _allAlbums.OrderBy(a => a.Year),
        };

        Albums.Clear();
        foreach (var album in sorted)
        {
            Albums.Add(album);
        }
    }
}
