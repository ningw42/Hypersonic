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
            ViewModel.PlayAlbumCommand.Execute(album);
        }
    }
}
