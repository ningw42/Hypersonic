using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Core.Models;

namespace Hypersonic.ViewModels;

public partial class PlaylistsViewModel : ObservableRecipient, INavigationAware
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ILogger _log;
    private List<PlaylistInfo> _allPlaylists = [];

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortOrderTooltip))]
    private int selectedSortIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortOrderGlyph))]
    [NotifyPropertyChangedFor(nameof(SortOrderTooltip))]
    private bool isSortDescending;

    public string SortOrderGlyph => IsSortDescending ? "\uE74B" : "\uE74A";

    public bool IsSortOrderSupported => true;

    public string SortOrderTooltip => IsSortDescending ? "Sort descending" : "Sort ascending";

    public ObservableCollection<PlaylistInfo> Playlists { get; } = [];

    public List<string> SortOptions { get; } =
    [
        "Name",
        "Song Count",
        "Duration",
    ];

    public PlaylistsViewModel(ISubsonicClientService subsonicClientService, ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _log = logService.GetLogger(nameof(PlaylistsViewModel));
    }

    public async void OnNavigatedTo(object parameter)
    {
        if (_allPlaylists.Count == 0)
        {
            await LoadPlaylistsAsync();
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

    private async Task LoadPlaylistsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var client = _subsonicClientService.Client;
            if (client == null)
            {
                return;
            }

            var response = await client.GetPlaylistsAsync();
            var playlists = response.Playlists?.Playlist;
            if (playlists == null)
            {
                return;
            }

            _allPlaylists = [];

            foreach (var playlist in playlists)
            {
                _allPlaylists.Add(new PlaylistInfo
                {
                    Id = playlist.Id,
                    Name = playlist.Name,
                    SongCount = playlist.SongCount,
                    Duration = playlist.Duration,
                    CoverArt = playlist.CoverArt,
                    CoverArtUrl = !string.IsNullOrEmpty(playlist.CoverArt)
                        ? client.GetCoverArtUri(playlist.CoverArt, "300").ToString()
                        : null,
                });
            }

            _log.Info("Playlists fetched", new { Count = _allPlaylists.Count });

            ApplySorting();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _log.Error("Failed to load playlists", exception: ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplySorting()
    {
        if (_allPlaylists.Count == 0)
        {
            return;
        }

        IEnumerable<PlaylistInfo> sorted = SelectedSortIndex switch
        {
            1 => IsSortDescending
                ? _allPlaylists.OrderByDescending(p => p.SongCount)
                : _allPlaylists.OrderBy(p => p.SongCount),
            2 => IsSortDescending
                ? _allPlaylists.OrderByDescending(p => p.Duration)
                : _allPlaylists.OrderBy(p => p.Duration),
            _ => IsSortDescending
                ? _allPlaylists.OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                : _allPlaylists.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        Playlists.Clear();
        foreach (var playlist in sorted)
        {
            Playlists.Add(playlist);
        }
    }
}
