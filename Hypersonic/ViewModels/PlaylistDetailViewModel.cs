using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;
using Hypersonic.Models;

namespace Hypersonic.ViewModels;

public partial class PlaylistDetailViewModel : ObservableRecipient, INavigationAware
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly IPlaybackService _playbackService;
    private readonly ILogger _log;
    internal List<SongInfo> _allSongs = [];
    private NowPlayingTracker? _nowPlayingTracker;
    private bool _navigatedAway;

    [ObservableProperty]
    public partial string PlaylistName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? CoverArtUrl { get; set; }

    [ObservableProperty]
    public partial string Owner { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommentVisible))]
    public partial string? Comment { get; set; }

    public bool IsCommentVisible => !string.IsNullOrEmpty(Comment);

    [ObservableProperty]
    public partial int SongCount { get; set; }

    [ObservableProperty]
    public partial string TotalDuration { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public ObservableCollection<object> TrackListItems { get; } = [];

    public PlaylistDetailViewModel(ISubsonicClientService subsonicClientService, IPlaybackService playbackService, ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _playbackService = playbackService;
        _log = logService.GetLogger(nameof(PlaylistDetailViewModel));
    }

    public async void OnNavigatedTo(object parameter)
    {
        _navigatedAway = false;
        if (parameter is string playlistId)
        {
            await LoadPlaylistAsync(playlistId);
        }
    }

    public void OnNavigatedFrom()
    {
        _navigatedAway = true;
        _nowPlayingTracker?.Unsubscribe();
    }

    private async Task LoadPlaylistAsync(string playlistId)
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
            var response = await client.GetPlaylistAsync(playlistId);
            var playlist = response.Playlist;
            if (playlist == null)
            {
                ErrorMessage = "Playlist not found.";
                return;
            }

            _log.Info("Playlist fetched", new { PlaylistId = playlistId, playlist.Name, playlist.SongCount });

            PlaylistName = playlist.Name ?? string.Empty;
            Owner = playlist.Owner ?? string.Empty;
            Comment = playlist.Comment;
            SongCount = playlist.SongCount;
            CoverArtUrl = !string.IsNullOrEmpty(playlist.CoverArt)
                ? client.GetCoverArtUri(playlist.CoverArt, "600").ToString()
                : null;
            TotalDuration = FormatDuration(playlist.Duration);

            var entries = playlist.Entry ?? [];
            _allSongs = entries
                .Select(s => SongMapper.MapFromChild(s, client))
                .ToList();

            for (var i = 0; i < _allSongs.Count; i++)
            {
                _allSongs[i].PositionNumber = i + 1;
            }

            TrackListItems.Clear();
            foreach (var song in _allSongs)
            {
                TrackListItems.Add(song);
            }

            if (_navigatedAway) return;
            _nowPlayingTracker?.Unsubscribe();
            _nowPlayingTracker = new NowPlayingTracker(_playbackService, () => _allSongs);
            _nowPlayingTracker.Subscribe();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _log.Error("Failed to load playlist", new { PlaylistId = playlistId }, ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void PlayPlaylist()
    {
        if (_allSongs.Count == 0)
        {
            return;
        }

        _log.Info("Playing playlist", new { Playlist = PlaylistName, SongCount = _allSongs.Count });
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
        _log.Info("Playing playlist from track", new { Playlist = PlaylistName, TrackId = song.Id, song.Title, Index = index });
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
                CoverArtUri = s.CoverArtUri ?? (CoverArtUrl != null ? new Uri(CoverArtUrl) : null),
                StreamUri = s.StreamUri,
            };
        }).ToList();
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
