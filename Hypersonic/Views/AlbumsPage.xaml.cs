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
}
