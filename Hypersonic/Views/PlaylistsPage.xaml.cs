using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Views;

public sealed partial class PlaylistsPage : Page
{
    public PlaylistsViewModel ViewModel { get; }

    public PlaylistsPage()
    {
        ViewModel = App.GetService<PlaylistsViewModel>();
        InitializeComponent();
    }

    private void PlaylistItem_ButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is PlaylistInfo playlist)
        {
            var navigationService = App.GetService<INavigationService>();
            navigationService.NavigateTo(typeof(PlaylistDetailViewModel).FullName!, playlist.Id);
        }
    }
}
