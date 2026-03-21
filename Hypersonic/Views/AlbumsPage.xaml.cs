using Hypersonic.Contracts.Services;
using Hypersonic.Models;
using Hypersonic.ViewModels;

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

    private void AlbumsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AlbumInfo album)
        {
            var navigationService = App.GetService<INavigationService>();
            navigationService.NavigateTo(typeof(AlbumDetailViewModel).FullName!, album.Id);
        }
    }
}
