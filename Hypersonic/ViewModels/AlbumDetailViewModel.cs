using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Models;

namespace Hypersonic.ViewModels;

public partial class AlbumDetailViewModel : ObservableRecipient, INavigationAware
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly IPlaybackService _playbackService;
    private List<SongInfo> _allSongs = [];

    [ObservableProperty]
    private string albumName = string.Empty;

    [ObservableProperty]
    private string albumArtist = string.Empty;

    [ObservableProperty]
    private string? coverArtUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReleaseDateGenre))]
    private string releaseDate = string.Empty;

    [ObservableProperty]
    private int songCount;

    [ObservableProperty]
    private string totalDuration = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReleaseDateGenre))]
    private string? genre;

    public string ReleaseDateGenre
    {
        get
        {
            if (!string.IsNullOrEmpty(ReleaseDate) && !string.IsNullOrEmpty(Genre))
            {
                return $"{ReleaseDate} \u00b7 {Genre}";
            }
            return ReleaseDate ?? Genre ?? string.Empty;
        }
    }

    [ObservableProperty]
    private bool isMultiDisc;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    public ObservableCollection<object> TrackListItems { get; } = [];

    public AlbumDetailViewModel(ISubsonicClientService subsonicClientService, IPlaybackService playbackService)
    {
        _subsonicClientService = subsonicClientService;
        _playbackService = playbackService;
    }

    public async void OnNavigatedTo(object parameter)
    {
        if (parameter is string albumId)
        {
            await LoadAlbumAsync(albumId);
        }
    }

    public void OnNavigatedFrom()
    {
    }

    private async Task LoadAlbumAsync(string albumId)
    {
        var client = _subsonicClientService.Client;
        if (client == null)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var response = await client.GetAlbumAsync(albumId);
            var album = response.Album;
            if (album == null)
            {
                ErrorMessage = "Album not found.";
                return;
            }

            AlbumName = album.Name ?? string.Empty;
            AlbumArtist = album.DisplayArtist ?? album.Artist ?? string.Empty;
            Genre = album.Genre;
            SongCount = album.SongCount;
            CoverArtUrl = !string.IsNullOrEmpty(album.CoverArt)
                ? client.GetCoverArtUri(album.CoverArt, "600").ToString()
                : null;

            ReleaseDate = FormatReleaseDate(album.ReleaseDate, album.OriginalReleaseDate, album.Year);
            TotalDuration = FormatDuration(album.Duration);

            var songs = album.Song ?? [];
            _allSongs = songs
                .OrderBy(s => s.DiscNumber)
                .ThenBy(s => s.Track)
                .Select(s => MapSongInfo(s, client))
                .ToList();

            var discNumbers = _allSongs.Select(s => s.DiscNumber).Distinct().ToList();
            IsMultiDisc = discNumbers.Count > 1;

            TrackListItems.Clear();

            if (IsMultiDisc)
            {
                foreach (var discNumber in discNumbers)
                {
                    var discTitle = album.DiscTitles?
                        .FirstOrDefault(d => d.Disc == discNumber)?.Title;
                    var headerText = !string.IsNullOrEmpty(discTitle)
                        ? $"Disc {discNumber} \u2014 {discTitle}"
                        : $"Disc {discNumber}";

                    TrackListItems.Add(new DiscHeaderItem { Title = headerText });

                    foreach (var song in _allSongs.Where(s => s.DiscNumber == discNumber))
                    {
                        TrackListItems.Add(song);
                    }
                }
            }
            else
            {
                foreach (var song in _allSongs)
                {
                    TrackListItems.Add(song);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void PlayAlbum()
    {
        if (_allSongs.Count == 0)
        {
            return;
        }

        _playbackService.PlayQueue(BuildPlaybackTracks(), 0);
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
        _playbackService.PlayQueue(tracks, index >= 0 ? index : 0);
    }

    private List<PlaybackTrack> BuildPlaybackTracks()
    {
        return _allSongs.Select(s => new PlaybackTrack
        {
            Id = s.Id,
            Title = s.Title,
            Artist = s.Artist,
            Album = AlbumName,
            Duration = s.Duration,
            TrackNumber = s.TrackNumber,
            DiscNumber = s.DiscNumber,
            CoverArtUri = s.CoverArtUri ?? (CoverArtUrl != null ? new Uri(CoverArtUrl) : null),
            StreamUri = s.StreamUri,
        }).ToList();
    }

    private static SongInfo MapSongInfo(Client.Models.Entities.Child song, Client.ISubsonicClient client)
    {
        return new SongInfo
        {
            Id = song.Id,
            TrackNumber = song.Track,
            Title = song.Title ?? string.Empty,
            Duration = song.Duration,
            DiscNumber = song.DiscNumber,
            Artist = song.Artist ?? string.Empty,
            Album = song.Album ?? string.Empty,
            Genre = song.Genre,
            Year = song.Year,
            CoverArt = song.CoverArt,
            CoverArtUrl = !string.IsNullOrEmpty(song.CoverArt)
                ? client.GetCoverArtUri(song.CoverArt, "300").ToString()
                : null,
            BitRate = song.BitRate,
            BitDepth = song.BitDepth,
            SamplingRate = song.SamplingRate,
            Suffix = song.Suffix,
            Size = song.Size,
            Path = song.Path,
            AlbumId = song.AlbumId,
            ArtistId = song.ArtistId,
            DisplayArtist = song.DisplayArtist,
            StreamUri = client.GetStreamUri(song.Id),
            CoverArtUri = !string.IsNullOrEmpty(song.CoverArt)
                ? client.GetCoverArtUri(song.CoverArt, "300")
                : null,
        };
    }

    private static string FormatReleaseDate(Client.Models.Entities.ItemDate? releaseDate, Client.Models.Entities.ItemDate? originalReleaseDate, int year)
    {
        var date = releaseDate ?? originalReleaseDate;
        if (date != null && date.Year > 0)
        {
            if (date.Month > 0 && date.Day > 0)
            {
                return $"{date.Year}-{date.Month:D2}-{date.Day:D2}";
            }
            return date.Year.ToString();
        }

        return year > 0 ? year.ToString() : string.Empty;
    }

    private static string FormatDuration(int totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours} hr {ts.Minutes} min";
        }
        return $"{ts.Minutes} min";
    }
}
