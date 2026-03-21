using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Models;
using System.Collections.ObjectModel;

namespace Hypersonic.ViewModels;

public partial class AlbumsViewModel : ObservableRecipient, INavigationAware
{
    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    public ObservableCollection<AlbumInfo> Albums { get; } = new();

    public AlbumsViewModel()
    {
        // Load sample data on initialization
        LoadSampleData();
    }

    private void LoadSampleData()
    {
        Albums.Clear();
        
        var sampleAlbums = new[]
        {
            new AlbumInfo { Id = "1", Name = "Dark Side of the Moon", Artist = "Pink Floyd", Year = 1973, SongCount = 9 },
            new AlbumInfo { Id = "2", Name = "Abbey Road", Artist = "The Beatles", Year = 1969, SongCount = 17 },
            new AlbumInfo { Id = "3", Name = "Random Access Memories", Artist = "Daft Punk", Year = 2013, SongCount = 13 },
            new AlbumInfo { Id = "4", Name = "OK Computer", Artist = "Radiohead", Year = 1997, SongCount = 12 },
            new AlbumInfo { Id = "5", Name = "The Wall", Artist = "Pink Floyd", Year = 1979, SongCount = 26 },
            new AlbumInfo { Id = "6", Name = "Rumours", Artist = "Fleetwood Mac", Year = 1977, SongCount = 11 },
            new AlbumInfo { Id = "7", Name = "Thriller", Artist = "Michael Jackson", Year = 1982, SongCount = 9 },
            new AlbumInfo { Id = "8", Name = "The Joshua Tree", Artist = "U2", Year = 1987, SongCount = 11 },
            new AlbumInfo { Id = "9", Name = "Back in Black", Artist = "AC/DC", Year = 1980, SongCount = 10 },
            new AlbumInfo { Id = "10", Name = "Hotel California", Artist = "Eagles", Year = 1976, SongCount = 9 },
            new AlbumInfo { Id = "11", Name = "Led Zeppelin IV", Artist = "Led Zeppelin", Year = 1971, SongCount = 8 },
            new AlbumInfo { Id = "12", Name = "Nevermind", Artist = "Nirvana", Year = 1991, SongCount = 12 }
        };

        foreach (var album in sampleAlbums)
        {
            Albums.Add(album);
        }
    }

    public async void OnNavigatedTo(object parameter)
    {
        await LoadAlbumsAsync();
    }

    public void OnNavigatedFrom()
    {
        // Clean up if needed
    }

    [RelayCommand]
    private async Task LoadAlbumsAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            
            // Simulate loading delay
            await Task.Delay(1000);
            
            // For now, just reload sample data
            // TODO: Replace with actual SubsonicClient call
            // var response = await _subsonicClient.GetAlbumList2Async("newest", 50);
            // Albums.Clear();
            // foreach (var album in response.AlbumList2.Album)
            // {
            //     Albums.Add(new AlbumInfo 
            //     { 
            //         Id = album.Id, 
            //         Name = album.Name, 
            //         Artist = album.Artist, 
            //         Year = album.Year ?? 0 
            //     });
            // }
            
            LoadSampleData();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load albums: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAlbumsAsync();
    }

    [RelayCommand]
    private void SelectAlbum(AlbumInfo? album)
    {
        if (album != null)
        {
            // TODO: Navigate to album details or play album
            // This would typically navigate to an album details page
        }
    }
}
