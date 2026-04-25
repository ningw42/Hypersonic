using System.Reflection;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Helpers;
using Hypersonic.Core.Models;

using Microsoft.UI.Xaml;

using Windows.ApplicationModel;

namespace Hypersonic.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly IBackdropSelectorService _backdropSelectorService;
    private readonly IScrobblingService _scrobblingService;
    private bool _isInitializingScrobbling;

    [ObservableProperty]
    public partial ElementTheme ElementTheme { get; set; }

    [ObservableProperty]
    public partial BackdropType BackdropType { get; set; }

    [ObservableProperty]
    public partial string VersionDescription { get; set; }

    [ObservableProperty]
    public partial bool IsScrobblingEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsNowPlayingEnabled { get; set; }

    [ObservableProperty]
    public partial int SelectedPlaytimeThreshold { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentageThresholdText))]
    public partial double ScrobblePercentageThreshold { get; set; }

    [ObservableProperty]
    public partial ScrobbleConditionMode ScrobbleConditionMode { get; set; }

    public string PercentageThresholdText => $"{(int)ScrobblePercentageThreshold}%";

    public List<PlaytimePreset> PlaytimePresets { get; } =
    [
        new(15, "Settings_PlaytimePreset_15Seconds".GetLocalized()),
        new(30, "Settings_PlaytimePreset_30Seconds".GetLocalized()),
        new(60, "Settings_PlaytimePreset_1Minute".GetLocalized()),
        new(120, "Settings_PlaytimePreset_2Minutes".GetLocalized()),
        new(240, "Settings_PlaytimePreset_4Minutes".GetLocalized()),
    ];

    public ICommand SwitchThemeCommand
    {
        get;
    }

    public ICommand SwitchBackdropCommand
    {
        get;
    }

    public ICommand SwitchConditionModeCommand
    {
        get;
    }

    public SettingsViewModel(
        IThemeSelectorService themeSelectorService,
        IBackdropSelectorService backdropSelectorService,
        IScrobblingService scrobblingService)
    {
        _themeSelectorService = themeSelectorService;
        _backdropSelectorService = backdropSelectorService;
        _scrobblingService = scrobblingService;
        ElementTheme = _themeSelectorService.Theme;
        BackdropType = _backdropSelectorService.Backdrop;
        VersionDescription = GetVersionDescription();

        _isInitializingScrobbling = true;
        IsScrobblingEnabled = _scrobblingService.IsEnabled;
        IsNowPlayingEnabled = _scrobblingService.NowPlayingEnabled;
        SelectedPlaytimeThreshold = _scrobblingService.PlaytimeThreshold;
        ScrobblePercentageThreshold = _scrobblingService.PercentageThreshold;
        ScrobbleConditionMode = _scrobblingService.ConditionMode;
        _isInitializingScrobbling = false;

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

        SwitchConditionModeCommand = new RelayCommand<int>(
            async (param) =>
            {
                var mode = (ScrobbleConditionMode)param;
                if (ScrobbleConditionMode != mode)
                {
                    ScrobbleConditionMode = mode;
                    await _scrobblingService.SetConditionModeAsync(mode);
                }
            });
    }

    partial void OnIsScrobblingEnabledChanged(bool value)
    {
        if (!_isInitializingScrobbling)
            _ = _scrobblingService.SetEnabledAsync(value);
    }

    partial void OnIsNowPlayingEnabledChanged(bool value)
    {
        if (!_isInitializingScrobbling)
            _ = _scrobblingService.SetNowPlayingEnabledAsync(value);
    }

    partial void OnSelectedPlaytimeThresholdChanged(int value)
    {
        if (!_isInitializingScrobbling)
            _ = _scrobblingService.SetPlaytimeThresholdAsync(value);
    }

    partial void OnScrobblePercentageThresholdChanged(double value)
    {
        if (!_isInitializingScrobbling)
            _ = _scrobblingService.SetPercentageThresholdAsync((int)value);
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

    public record PlaytimePreset(int Seconds, string Display);
}
