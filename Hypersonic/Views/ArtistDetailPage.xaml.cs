using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Views;

public sealed partial class ArtistDetailPage : Page
{
    public ArtistDetailViewModel ViewModel { get; }

    public ArtistDetailPage()
    {
        ViewModel = App.GetService<ArtistDetailViewModel>();
        InitializeComponent();
    }

    private void AlbumCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is AlbumInfo album)
        {
            ViewModel.NavigateToAlbumCommand.Execute(album);
        }
    }

    private void SimilarArtistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is ArtistInfo artist)
        {
            ViewModel.NavigateToArtistCommand.Execute(artist);
        }
    }
}
