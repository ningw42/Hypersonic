using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Views;

public sealed partial class AlbumsPage : Page
{
    public AlbumsViewModel ViewModel
    {
        get;
    }

    public AlbumsPage()
    {
        ViewModel = App.GetService<AlbumsViewModel>();
        InitializeComponent();
    }

    private void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is AlbumInfo album)
        {
            var navigationService = App.GetService<INavigationService>();
            navigationService.NavigateTo(typeof(AlbumDetailViewModel).FullName!, album.Id);
        }
    }

    private void ArtistLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton link && link.DataContext is AlbumInfo album && !string.IsNullOrEmpty(album.ArtistId))
        {
            var navigationService = App.GetService<INavigationService>();
            navigationService.NavigateTo(typeof(ArtistDetailViewModel).FullName!, album.ArtistId);
        }
    }

    private async void AlbumsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
        {
            return;
        }

        var scrollViewer = (ScrollViewer)sender;
        var distanceToEnd = scrollViewer.ExtentHeight - (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight);

        if (distanceToEnd <= 2.0 * scrollViewer.ViewportHeight &&
            ViewModel.Albums.HasMoreItems)
        {
            await ViewModel.Albums.LoadMoreItemsAsync(50);
        }
    }
}
