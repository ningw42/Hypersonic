using Hypersonic.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Views;

public sealed partial class AlbumDetailPage : Page
{
    public AlbumDetailViewModel ViewModel { get; }

    public AlbumDetailPage()
    {
        ViewModel = App.GetService<AlbumDetailViewModel>();
        InitializeComponent();
    }

    private void TrackList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SongInfo song)
        {
            ViewModel.PlayFromTrackCommand.Execute(song);
        }
    }
}
