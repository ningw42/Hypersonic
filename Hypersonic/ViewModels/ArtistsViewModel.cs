using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Models;

namespace Hypersonic.ViewModels;

public partial class ArtistsViewModel : ObservableRecipient, INavigationAware
{
    private readonly ISubsonicClientService _subsonicClientService;
    private List<ArtistInfo> _allArtists = [];

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

    public ObservableCollection<ArtistInfo> Artists { get; } = [];

    public List<string> SortOptions { get; } =
    [
        "Name",
        "Album Count",
        "Rating",
    ];

    public ArtistsViewModel(ISubsonicClientService subsonicClientService)
    {
        _subsonicClientService = subsonicClientService;
    }

    public async void OnNavigatedTo(object parameter)
    {
        if (_allArtists.Count == 0)
        {
            await LoadArtistsAsync();
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

    private async Task LoadArtistsAsync()
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

            var response = await client.GetArtistsAsync();
            var indexes = response.Artists?.Index;
            if (indexes == null)
            {
                return;
            }

            _allArtists = [];

            foreach (var index in indexes)
            {
                if (index.Artist == null)
                {
                    continue;
                }

                foreach (var artist in index.Artist)
                {
                    _allArtists.Add(new ArtistInfo
                    {
                        Id = artist.Id,
                        Name = artist.Name,
                        SortName = artist.SortName,
                        AlbumCount = artist.AlbumCount,
                        UserRating = artist.UserRating,
                        CoverArt = artist.CoverArt,
                        CoverArtUrl = !string.IsNullOrEmpty(artist.CoverArt)
                            ? client.GetCoverArtUri(artist.CoverArt, "300").ToString()
                            : null,
                    });
                }
            }

            ApplySorting();
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

    private void ApplySorting()
    {
        if (_allArtists.Count == 0)
        {
            return;
        }

        IEnumerable<ArtistInfo> sorted = SelectedSortIndex switch
        {
            1 => IsSortDescending
                ? _allArtists.OrderByDescending(a => a.AlbumCount)
                : _allArtists.OrderBy(a => a.AlbumCount),
            2 => IsSortDescending
                ? _allArtists.OrderByDescending(a => a.UserRating)
                : _allArtists.OrderBy(a => a.UserRating),
            _ => IsSortDescending
                ? _allArtists.OrderByDescending(a => a.SortName ?? a.Name, StringComparer.CurrentCultureIgnoreCase)
                : _allArtists.OrderBy(a => a.SortName ?? a.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        Artists.Clear();
        foreach (var artist in sorted)
        {
            Artists.Add(artist);
        }
    }
}
