using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Core.Models;
using Hypersonic.Models;

namespace Hypersonic.ViewModels;

public partial class AlbumDetailViewModel : ObservableRecipient, INavigationAware
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly IPlaybackService _playbackService;
    private readonly INavigationService _navigationService;
    private readonly ILogger _log;
    private List<SongInfo> _allSongs = [];

    [ObservableProperty]
    public partial string AlbumName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AlbumArtist { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AlbumArtistId { get; set; }

    [ObservableProperty]
    public partial string? AlbumArtistImageUrl { get; set; }

    [ObservableProperty]
    public partial List<SongArtistInfo> AlbumArtists { get; set; } = [];

    [ObservableProperty]
    public partial string? AlbumDisplayArtist { get; set; }

    public bool AlbumHasStructuredArtists { get; private set; }

    [ObservableProperty]
    public partial string? CoverArtUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReleaseDateGenre))]
    public partial string ReleaseDate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SongCount { get; set; }

    [ObservableProperty]
    public partial string TotalDuration { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReleaseDateGenre))]
    public partial string? Genre { get; set; }

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
    public partial bool IsMultiDisc { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public ObservableCollection<object> TrackListItems { get; } = [];

    public AlbumDetailViewModel(ISubsonicClientService subsonicClientService, IPlaybackService playbackService, INavigationService navigationService, ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _playbackService = playbackService;
        _navigationService = navigationService;
        _log = logService.GetLogger(nameof(AlbumDetailViewModel));
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

            _log.Info("Album fetched", new { AlbumId = albumId, album.Name, SongCount = album.SongCount });

            AlbumName = album.Name ?? string.Empty;
            AlbumArtist = album.DisplayArtist ?? album.Artist ?? string.Empty;
            AlbumArtistId = album.ArtistId;
            Genre = album.Genre;
            SongCount = album.SongCount;

            AlbumDisplayArtist = album.DisplayArtist;
            var (albumArtists, albumHasStructured) = SongArtistMapper.MapArtists(
                album.Artists?.Select(a => (a.Name, (string?)a.Id)),
                album.Artist,
                album.ArtistId);
            AlbumHasStructuredArtists = albumHasStructured;
            AlbumArtists = albumArtists;
            CoverArtUrl = !string.IsNullOrEmpty(album.CoverArt)
                ? client.GetCoverArtUri(album.CoverArt, "600").ToString()
                : null;

            // Load artist image in background (best-effort)
            if (!string.IsNullOrEmpty(album.ArtistId))
            {
                _ = LoadArtistImageAsync(client, album.ArtistId);
            }

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

                    TrackListItems.Add(new DiscHeaderItem { Title = headerText, DiscNumber = discNumber });

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
            _log.Error("Failed to load album", new { AlbumId = albumId }, ex);
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

        _log.Info("Playing album", new { Album = AlbumName, SongCount = _allSongs.Count });
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
        _log.Info("Playing album from track", new { Album = AlbumName, TrackId = song.Id, song.Title, Index = index });
        _playbackService.PlayQueue(tracks, index >= 0 ? index : 0);
    }

    [RelayCommand]
    private void ToggleDiscExpansion(DiscHeaderItem? header)
    {
        if (header == null)
        {
            return;
        }

        header.IsExpanded = !header.IsExpanded;

        if (header.IsExpanded)
        {
            var headerIndex = TrackListItems.IndexOf(header);
            var songsForDisc = _allSongs.Where(s => s.DiscNumber == header.DiscNumber).ToList();
            for (var i = 0; i < songsForDisc.Count; i++)
            {
                TrackListItems.Insert(headerIndex + 1 + i, songsForDisc[i]);
            }
        }
        else
        {
            var songsToRemove = _allSongs.Where(s => s.DiscNumber == header.DiscNumber).ToList();
            foreach (var song in songsToRemove)
            {
                TrackListItems.Remove(song);
            }
        }
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

    [RelayCommand]
    private void NavigateToArtist()
    {
        if (!string.IsNullOrEmpty(AlbumArtistId))
        {
            _navigationService.NavigateTo(typeof(ArtistDetailViewModel).FullName!, AlbumArtistId);
        }
    }

    private async Task LoadArtistImageAsync(Client.ISubsonicClient client, string artistId)
    {
        try
        {
            var response = await client.GetArtistAsync(artistId);
            var artist = response.Artist;
            if (artist != null)
            {
                AlbumArtistImageUrl = !string.IsNullOrEmpty(artist.ArtistImageUrl)
                    ? artist.ArtistImageUrl
                    : !string.IsNullOrEmpty(artist.CoverArt)
                        ? client.GetCoverArtUri(artist.CoverArt, "100").ToString()
                        : null;
            }
        }
        catch (Exception)
        {
            _log.Debug("Failed to load artist image", new { ArtistId = artistId });
            // Best-effort; artist image is non-critical
        }
    }

    private List<PlaybackTrack> BuildPlaybackTracks()
    {
        return _allSongs.Select(s => new PlaybackTrack
        {
            Id = s.Id,
            Title = s.Title,
            Artist = s.Artist,
            ArtistId = s.ArtistId,
            Album = AlbumName,
            AlbumId = s.AlbumId,
            Duration = s.Duration,
            TrackNumber = s.TrackNumber,
            DiscNumber = s.DiscNumber,
            CoverArtUri = s.CoverArtUri ?? (CoverArtUrl != null ? new Uri(CoverArtUrl) : null),
            StreamUri = s.StreamUri,
        }).ToList();
    }

    private static SongInfo MapSongInfo(Client.Models.Entities.Child song, Client.ISubsonicClient client)
    {
        var (artists, hasStructured) = SongArtistMapper.MapArtists(
            song.Artists?.Select(a => (a.Name, (string?)a.Id)),
            song.Artist,
            song.ArtistId);

        return new SongInfo
        {
            Id = song.Id,
            TrackNumber = song.Track,
            Title = song.Title ?? string.Empty,
            Duration = song.Duration,
            DiscNumber = song.DiscNumber,
            Artist = song.Artist ?? string.Empty,
            Artists = artists,
            HasStructuredArtists = hasStructured,
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
            PlayCount = song.PlayCount,
            BPM = song.BPM,
            IsStarred = !string.IsNullOrEmpty(song.Starred),
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
