using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

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

public partial class ArtistDetailViewModel : ObservableRecipient, INavigationAware
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly INavigationService _navigationService;
    private readonly IPlaybackService _playbackService;
    private readonly ILogger _log;
    private List<AlbumInfo> _allAlbums = [];
    private List<SongInfo> _communityTopSongs = [];
    private List<SongInfo> _personalTopSongs = [];
    private NowPlayingTracker? _nowPlayingTracker;
    private bool _navigatedAway;

    [ObservableProperty]
    public partial string ArtistName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ImageReference? ArtistImage { get; set; }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopSongsSectionVisible))]
    public partial bool IsTopSongsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopSongsSectionVisible))]
    public partial bool HasTopSongs { get; set; }

    public bool IsTopSongsSectionVisible => IsTopSongsLoading || HasTopSongs;

    [ObservableProperty]
    public partial bool IsCommunityAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsPersonalAvailable { get; set; }

    [ObservableProperty]
    public partial int SelectedTopSongsMode { get; set; }

    public string SortOrderGlyph => IsSortDescending ? "\uE74B" : "\uE74A";

    public string SortOrderTooltip => IsSortDescending ? "Sort descending" : "Sort ascending";

    public string AlbumCountText => AlbumCount == 1 ? "1 album" : $"{AlbumCount} albums";

    public string TotalSongCountText => TotalSongCount == 1 ? "1 song" : $"{TotalSongCount} songs";

    public int BiographyMaxLines => IsBiographyExpanded ? 999 : 3;

    public string BiographyToggleText => IsBiographyExpanded ? "Show less" : "Show more";

    public string? CurrentArtistId { get; private set; }

    public ObservableCollection<AlbumInfo> Albums { get; } = [];

    public ObservableCollection<ArtistInfo> SimilarArtists { get; } = [];

    public ObservableCollection<SongInfo> TopSongs { get; } = [];

    public List<string> SortOptions { get; } =
    [
        "Release Date",
        "Name",
    ];

    public ArtistDetailViewModel(
        ISubsonicClientService subsonicClientService,
        INavigationService navigationService,
        IPlaybackService playbackService,
        ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _navigationService = navigationService;
        _playbackService = playbackService;
        _log = logService.GetLogger(nameof(ArtistDetailViewModel));
    }

    public async void OnNavigatedTo(object parameter)
    {
        _navigatedAway = false;
        if (parameter is string artistId)
        {
            await LoadArtistAsync(artistId);
        }
    }

    public void OnNavigatedFrom()
    {
        _navigatedAway = true;
        _nowPlayingTracker?.Unsubscribe();
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

    [RelayCommand]
    private void NavigateToAlbumById(string? albumId)
    {
        if (string.IsNullOrEmpty(albumId))
        {
            return;
        }

        _navigationService.NavigateTo(typeof(AlbumDetailViewModel).FullName!, albumId);
    }

    [RelayCommand]
    private void NavigateToArtistById(string? artistId)
    {
        if (string.IsNullOrEmpty(artistId))
        {
            return;
        }

        _navigationService.NavigateTo(typeof(ArtistDetailViewModel).FullName!, artistId);
    }

    [RelayCommand]
    private void PlayFromTopSong(SongInfo? song)
    {
        if (song == null || TopSongs.Count == 0)
        {
            return;
        }

        var tracks = BuildTopSongsPlaybackTracks();
        var index = TopSongs.IndexOf(song);
        _log.Info("Playing top songs from track", new { TrackId = song.Id, song.Title, Index = index });
        _playbackService.PlayQueue(tracks, index >= 0 ? index : 0);
    }

    [RelayCommand]
    private async Task ToggleTopSongStarAsync(SongInfo? song)
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

        SyncStarInBothCaches(song.Id, !wasStarred);

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
            SyncStarInBothCaches(song.Id, wasStarred);
            _log.Warning("Failed to toggle star", new { SongId = song.Id, wasStarred }, ex);
        }
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

        var loadedSuccessfully = false;
        Task<Client.Models.Responses.GetArtistInfo2Response>? infoTask = null;

        try
        {
            var artistTask = client.GetArtistAsync(artistId);
            infoTask = client.GetArtistInfo2Async(artistId);

            await artistTask;

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
            _allAlbums = AlbumMapper.MapAll(albums, client);

            TotalSongCount = _allAlbums.Sum(a => a.SongCount ?? 0);

            ArtistImage =
                ImageReference.Remote(artist.ArtistImageUrl) ??
                ImageReference.SubsonicCoverArt(artist.CoverArt);

            // Process artist info (biography, similar artists, image fallback)
            try
            {
                await infoTask;
                var info = infoTask.Result.ArtistInfo2;
                if (info != null)
                {
                    if (!string.IsNullOrWhiteSpace(info.Biography))
                    {
                        Biography = Regex.Replace(info.Biography, "<.*?>", string.Empty).Trim();
                        HasBiography = !string.IsNullOrWhiteSpace(Biography);
                    }

                    if (ArtistImage == null)
                    {
                        ArtistImage = ImageReference.Remote(info.LargeImageUrl ?? info.MediumImageUrl);
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
                                CoverImage = ImageReference.SubsonicCoverArt(similar.CoverArt),
                            });
                        }

                        HasSimilarArtists = true;
                    }
                }
            }
            catch (Exception)
            {
                _log.Debug("GetArtistInfo2 unavailable");
            }

            ApplySorting();
            loadedSuccessfully = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _log.Error("Failed to load artist", new { ArtistId = artistId }, ex);
        }
        finally
        {
            IsLoading = false;

            if (infoTask != null)
            {
                try { await infoTask; } catch { }
            }
        }

        if (loadedSuccessfully)
        {
            await LoadTopSongsAsync(client, artistId);
        }
    }

    private async Task LoadTopSongsAsync(Client.ISubsonicClient client, string artistId)
    {
        IsTopSongsLoading = true;

        try
        {
            var communityTask = LoadCommunityTopSongsAsync(client);
            var personalTask = LoadPersonalTopSongsAsync(client, artistId);

            await Task.WhenAll(communityTask, personalTask);

            _communityTopSongs = communityTask.Result;
            _personalTopSongs = personalTask.Result;

            IsCommunityAvailable = _communityTopSongs.Count > 0;
            IsPersonalAvailable = _personalTopSongs.Count > 0;

            if (IsCommunityAvailable || IsPersonalAvailable)
            {
                SelectedTopSongsMode = IsCommunityAvailable ? 0 : 1;
                ApplyTopSongsMode();
                HasTopSongs = true;

                if (!_navigatedAway)
                {
                    _nowPlayingTracker?.Unsubscribe();
                    _nowPlayingTracker = new NowPlayingTracker(
                        _playbackService,
                        () => _communityTopSongs.Concat(_personalTopSongs));
                    _nowPlayingTracker.Subscribe();
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to load top songs", new { ArtistId = artistId }, ex);
        }
        finally
        {
            IsTopSongsLoading = false;
        }
    }

    private async Task<List<SongInfo>> LoadCommunityTopSongsAsync(Client.ISubsonicClient client)
    {
        try
        {
            var response = await client.GetTopSongsAsync(ArtistName, count: 20);
            var songs = response.TopSongs?.Song;
            if (songs == null || songs.Length == 0)
            {
                return [];
            }

            var result = songs.Select((s, i) =>
            {
                var info = SongMapper.MapFromChild(s, client);
                info.PositionNumber = i + 1;
                return info;
            }).ToList();

            _log.Info("Community top songs loaded", new { Count = result.Count });
            return result;
        }
        catch (Exception ex)
        {
            _log.Warning("Community top songs unavailable", exception: ex);
            return [];
        }
    }

    private async Task<List<SongInfo>> LoadPersonalTopSongsAsync(Client.ISubsonicClient client, string artistId)
    {
        try
        {
            var albumIds = _allAlbums.Select(a => a.Id).ToList();
            if (albumIds.Count == 0)
            {
                return [];
            }

            var semaphore = new SemaphoreSlim(5);
            var allChildren = new List<(Client.Models.Entities.Child Song, string? AlbumVersion)>();
            var lockObj = new Lock();

            var tasks = albumIds.Select(async albumId =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var albumResponse = await client.GetAlbumAsync(albumId);
                    var album = albumResponse.Album;
                    var songs = album?.Song;
                    if (songs != null)
                    {
                        lock (lockObj)
                        {
                            foreach (var song in songs)
                            {
                                allChildren.Add((song, album?.Version));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Debug("Failed to fetch album for personal top songs", new { AlbumId = albumId, Error = ex.Message });
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            var artistName = ArtistName;
            var filtered = allChildren
                .Where(c => IsArtistCredited(c.Song, artistId, artistName) && c.Song.PlayCount > 0)
                .OrderByDescending(c => c.Song.PlayCount)
                .Take(20)
                .Select((c, i) =>
                {
                    var info = SongMapper.MapFromChild(c.Song, client, c.AlbumVersion);
                    info.PositionNumber = i + 1;
                    return info;
                })
                .ToList();

            _log.Info("Personal top songs loaded", new { Count = filtered.Count, TotalSongsScanned = allChildren.Count });
            return filtered;
        }
        catch (Exception ex)
        {
            _log.Warning("Personal top songs unavailable", exception: ex);
            return [];
        }
    }

    internal static bool IsArtistCredited(Client.Models.Entities.Child child, string artistId, string artistName)
    {
        if (child.Artists != null)
        {
            foreach (var a in child.Artists)
            {
                if (!string.IsNullOrEmpty(a.Id) && a.Id == artistId)
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrEmpty(child.ArtistId) && child.ArtistId == artistId)
        {
            return true;
        }

        if (child.Artists != null)
        {
            foreach (var a in child.Artists)
            {
                if (!string.IsNullOrEmpty(a.Id))
                {
                    continue;
                }

                if (string.Equals(a.Name, artistName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (string.Equals(child.Artist, artistName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    internal void ApplyTopSongsMode()
    {
        var source = SelectedTopSongsMode == 0 ? _communityTopSongs : _personalTopSongs;
        TopSongs.Clear();
        foreach (var song in source)
        {
            TopSongs.Add(song);
        }
    }

    private void SyncStarInBothCaches(string songId, bool isStarred)
    {
        foreach (var song in _communityTopSongs)
        {
            if (song.Id == songId)
            {
                song.IsStarred = isStarred;
            }
        }

        foreach (var song in _personalTopSongs)
        {
            if (song.Id == songId)
            {
                song.IsStarred = isStarred;
            }
        }
    }

    private List<PlaybackTrack> BuildTopSongsPlaybackTracks()
    {
        return TopSongs.Select(s =>
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
