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
    private int _offset;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private int selectedSortIndex;

    public IncrementalAlbumCollection Albums { get; }

    public List<string> SortOptions { get; } =
    [
        "Added Date",
        "Release Date",
        "Name",
        "Album Artist",
    ];

    public AlbumsViewModel(ISubsonicClientService subsonicClientService)
    {
        _subsonicClientService = subsonicClientService;
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

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAlbumsAsync(reset: true);
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

        var result = new List<AlbumInfo>(albums.Length);
        foreach (var album in albums)
        {
            result.Add(new AlbumInfo
            {
                Id = album.Id,
                Name = album.Name,
                Artist = album.Artist,
                Year = album.Year,
                CoverArt = album.CoverArt,
                CoverArtUri = !string.IsNullOrEmpty(album.CoverArt)
                    ? client.GetCoverArtUri(album.CoverArt, "300")
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
            1 => ("byYear", 9999, 0),           // Release Date desc
            2 => ("alphabeticalByName", null, null),
            3 => ("alphabeticalByArtist", null, null),
            _ => ("newest", null, null),          // Added Date desc (default)
        };
    }
}
