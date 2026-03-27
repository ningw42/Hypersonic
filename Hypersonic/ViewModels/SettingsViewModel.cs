using System.Reflection;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Helpers;
using Hypersonic.Models;

using Microsoft.UI.Xaml;

using Windows.ApplicationModel;

namespace Hypersonic.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly IBackdropSelectorService _backdropSelectorService;

    [ObservableProperty]
    private ElementTheme _elementTheme;

    [ObservableProperty]
    private BackdropType _backdropType;

    [ObservableProperty]
    private string _versionDescription;

    public ICommand SwitchThemeCommand
    {
        get;
    }

    public ICommand SwitchBackdropCommand
    {
        get;
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService, IBackdropSelectorService backdropSelectorService)
    {
        _themeSelectorService = themeSelectorService;
        _backdropSelectorService = backdropSelectorService;
        _elementTheme = _themeSelectorService.Theme;
        _backdropType = _backdropSelectorService.Backdrop;
        _versionDescription = GetVersionDescription();

        SwitchThemeCommand = new RelayCommand<ElementTheme>(
            async (param) =>
            {
                if (ElementTheme != param)
                {
                    ElementTheme = param;
                    await _themeSelectorService.SetThemeAsync(param);
                }
            });

        SwitchBackdropCommand = new RelayCommand<int>(
            async (param) =>
            {
                var backdrop = (BackdropType)param;
                if (BackdropType != backdrop)
                {
                    BackdropType = backdrop;
                    await _backdropSelectorService.SetBackdropAsync(backdrop);
                }
            });
    }

    private static string GetVersionDescription()
    {
        Version version;

        if (RuntimeHelper.IsMSIX)
        {
            var packageVersion = Package.Current.Id.Version;

            version = new(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }
        else
        {
            version = Assembly.GetExecutingAssembly().GetName().Version!;
        }

        return $"{"AppDisplayName".GetLocalized()} - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
