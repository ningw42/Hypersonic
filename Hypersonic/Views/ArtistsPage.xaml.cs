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
        // Future: navigate to ArtistDetailPage
    }
}
