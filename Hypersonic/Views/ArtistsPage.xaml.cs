using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Views;

public sealed partial class ArtistsPage : Page
{
    public ArtistsViewModel ViewModel { get; }

    public ArtistsPage()
    {
        ViewModel = App.GetService<ArtistsViewModel>();
        InitializeComponent();
    }

    private void ArtistCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is ArtistInfo artist)
        {
            var navigationService = App.GetService<INavigationService>();
            navigationService.NavigateTo(typeof(ArtistDetailViewModel).FullName!, artist.Id);
        }
    }
}
