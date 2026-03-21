using Hypersonic.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Views;

public sealed partial class ConnectPage : Page
{
    public ConnectViewModel ViewModel
    {
        get;
    }

    public ConnectPage()
    {
        ViewModel = App.GetService<ConnectViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadSavedCredentialsAsync();
    }
}
