using Hypersonic.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Views;

public sealed partial class PlaylistDetailPage : Page
{
    public PlaylistDetailViewModel ViewModel { get; }

    public PlaylistDetailPage()
    {
        ViewModel = App.GetService<PlaylistDetailViewModel>();
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
