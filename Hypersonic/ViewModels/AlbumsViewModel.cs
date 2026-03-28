using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Collections;
using Hypersonic.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Models;

namespace Hypersonic.ViewModels;

public partial class AlbumsViewModel : ObservableRecipient, INavigationAware
{
    private const int PageSize = 50;

    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ILogger _log;
    private int _offset;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortOrderSupported))]
    [NotifyPropertyChangedFor(nameof(SortOrderTooltip))]
    private int selectedSortIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortOrderGlyph))]
    [NotifyPropertyChangedFor(nameof(IsSortOrderSupported))]
    [NotifyPropertyChangedFor(nameof(SortOrderTooltip))]
    private bool isSortDescending = true;

    public string SortOrderGlyph => IsSortDescending ? "\uE74B" : "\uE74A";

    public bool IsSortOrderSupported => SelectedSortIndex == 1; // Only Release Date supports both orders

    public string SortOrderTooltip => IsSortOrderSupported
        ? (IsSortDescending ? "Sort descending" : "Sort ascending")
        : "Sort order not supported for this sort type";

    public IncrementalAlbumCollection Albums { get; }

    public List<string> SortOptions { get; } =
    [
        "Added Date",
        "Release Date",
        "Name",
        "Album Artist",
    ];

    public AlbumsViewModel(ISubsonicClientService subsonicClientService, ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _log = logService.GetLogger(nameof(AlbumsViewModel));
        Albums = new IncrementalAlbumCollection(LoadMoreAlbumsAsync);
    }

    public async void OnNavigatedTo(object parameter)
    {
        if (Albums.Count == 0)
        {
            await LoadAlbumsAsync(reset: true);
        }
    }

    public void OnNavigatedFrom()
    {
    }

    partial void OnSelectedSortIndexChanged(int value)
    {
        _ = LoadAlbumsAsync(reset: true);
    }

    partial void OnIsSortDescendingChanged(bool value)
    {
        _ = LoadAlbumsAsync(reset: true);
    }

    [RelayCommand]
    private void ToggleSortOrder()
    {
        IsSortDescending = !IsSortDescending;
    }

    private async Task LoadAlbumsAsync(bool reset)
    {
        if (reset)
        {
            _offset = 0;
            Albums.Clear();
            Albums.ResetHasMore();
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var items = await LoadMoreAlbumsAsync(PageSize);

            foreach (var item in items)
            {
                Albums.Add(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _log.Error("Failed to load albums", exception: ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<IList<AlbumInfo>> LoadMoreAlbumsAsync(uint count)
    {
        var client = _subsonicClientService.Client;
        if (client == null)
        {
            return [];
        }

        var (type, fromYear, toYear) = GetSortParameters();

        _log.Debug("Fetching albums", new { SortType = type, Offset = _offset, Count = count });

        var response = await client.GetAlbumList2Async(
            type,
            size: (int)count,
            offset: _offset,
            fromYear: fromYear,
            toYear: toYear);

        var albums = response.AlbumList2?.Album;
        if (albums == null || albums.Length == 0)
        {
            return [];
        }

        _offset += albums.Length;

        _log.Info("Albums fetched", new { Count = albums.Length, TotalOffset = _offset });

        var result = new List<AlbumInfo>(albums.Length);
        foreach (var album in albums)
        {
            result.Add(new AlbumInfo
            {
                Id = album.Id,
                Name = album.Name,
                Artist = album.Artist,
                ArtistId = album.ArtistId,
                Year = album.Year,
                CoverArt = album.CoverArt,
                CoverArtUrl = !string.IsNullOrEmpty(album.CoverArt)
                    ? client.GetCoverArtUri(album.CoverArt, "300").ToString()
                    : null,
                SongCount = album.SongCount,
                Duration = TimeSpan.FromSeconds(album.Duration),
            });
        }

        return result;
    }

    private (string type, int? fromYear, int? toYear) GetSortParameters()
    {
        return SelectedSortIndex switch
        {
            1 => IsSortDescending
                ? ("byYear", 9999, 0)
                : ("byYear", 0, 9999),
            2 => ("alphabeticalByName", null, null),
            3 => ("alphabeticalByArtist", null, null),
            _ => ("newest", null, null),
        };
    }
}
