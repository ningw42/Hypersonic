using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    // Persistence keys for the font customization settings. These live in the
    // local settings JSON alongside AppBackgroundRequestedTheme etc. and are
    // currently inert — saved on change, restored on construction, but not yet
    // wired to any visual font/scale application path.
    internal const string UiFontFamiliesKey = "AppUiFontFamilies";
    internal const string LyricsFontFamiliesKey = "AppLyricsFontFamilies";
    internal const string UiScaleKey = "AppUiScale";

    /// <summary>
    /// Discrete UI scale presets. Index of the user's chosen value is what the
    /// Segmented control binds to; the underlying double is what we persist so
    /// the preset list can be reordered or extended without breaking saved data.
    /// </summary>
    public static readonly IReadOnlyList<double> UiScalePresets = [0.9, 1.0, 1.15, 1.25, 1.5];

    private const double DefaultUiScale = 1.0;
    private const int DefaultUiScaleIndex = 1; // index of 1.0 in UiScalePresets

    private readonly IThemeSelectorService _themeSelectorService;
    private readonly IBackdropSelectorService _backdropSelectorService;
    private readonly IScrobblingService _scrobblingService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IAppStateService _appStateService;
    private readonly ITrayIconService _trayIconService;
    private readonly IImageService _imageService;
    private readonly object _fontSettingsSaveLock = new();
    private bool _isInitializingScrobbling;
    private bool _isInitializingGeneral;
    private bool _isInitializingFontSettings = true;
    private Task? _fontInitTask;
    private Task _fontSettingsSaveQueue = Task.CompletedTask;
    private List<string> _appliedUiFontFamilies = [];
    private List<string> _appliedLyricsFontFamilies = [];
    private double _appliedUiScale = DefaultUiScale;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElementThemeIndex))]
    public partial ElementTheme ElementTheme { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackdropTypeIndex))]
    public partial BackdropType BackdropType { get; set; }

    [ObservableProperty]
    public partial string VersionDescription { get; set; }

    [ObservableProperty]
    public partial bool IsScrobblingEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsNowPlayingEnabled { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial int SelectedPlaytimeThreshold { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentageThresholdText))]
    public partial double ScrobblePercentageThreshold { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScrobbleConditionModeIndex))]
    public partial ScrobbleConditionMode ScrobbleConditionMode { get; set; }

    public string PercentageThresholdText => $"{(int)ScrobblePercentageThreshold}%";

    /// <summary>
    /// Ordered list of font family names used as the UI font fallback chain.
    /// First entry takes precedence; subsequent entries cover missing glyphs.
    /// Mutations are persisted automatically via collection-changed events.
    /// </summary>
    public ObservableCollection<string> UiFontFamilies { get; } = [];

    /// <summary>
    /// Ordered list of font family names used as the lyrics font fallback chain.
    /// </summary>
    public ObservableCollection<string> LyricsFontFamilies { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UiScaleIndex))]
    [NotifyPropertyChangedFor(nameof(CanResetUiScale))]
    public partial double UiScale { get; set; }

    /// <summary>
    /// True when the current font collections differ from the values loaded at
    /// startup. Reverting all font changes clears the restart prompt.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFontRestartRequired { get; set; }

    [ObservableProperty]
    public partial bool IsFontSettingsInitialized { get; set; }

    [ObservableProperty]
    public partial string ImageCacheUsageText { get; set; } = "Calculating...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClearImageCache))]
    public partial bool IsImageCacheBusy { get; set; }

    public bool CanClearImageCache => !IsImageCacheBusy;

    /// <summary>
    /// SelectedIndex of the Segmented control that exposes <see cref="UiScale"/>.
    /// Round-trips to/from <see cref="UiScalePresets"/>; setting an out-of-range
    /// or unknown index is a no-op so the bound control can settle cleanly.
    /// </summary>
    public int UiScaleIndex
    {
        get
        {
            for (var i = 0; i < UiScalePresets.Count; i++)
            {
                if (Math.Abs(UiScalePresets[i] - UiScale) < 0.0001)
                {
                    return i;
                }
            }

            return DefaultUiScaleIndex;
        }
        set
        {
            if (value < 0 || value >= UiScalePresets.Count)
            {
                return;
            }

            UiScale = UiScalePresets[value];
        }
    }

    /// <summary>
    /// SelectedIndex of the Segmented control that exposes <see cref="ElementTheme"/>.
    /// Round-trips Light/Dark/Default to 0/1/2; out-of-range values (including
    /// Segmented's transient -1 during rebind) are no-ops so the bound control
    /// can settle cleanly. Setter fires-and-forgets the service call because
    /// property setters cannot await.
    /// </summary>
    public int ElementThemeIndex
    {
        get => ElementTheme switch
        {
            ElementTheme.Light => 0,
            ElementTheme.Dark => 1,
            ElementTheme.Default => 2,
            _ => 2,
        };
        set
        {
            if (value < 0 || value > 2)
            {
                return;
            }

            var theme = value switch
            {
                0 => ElementTheme.Light,
                1 => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            if (theme == ElementTheme)
            {
                return;
            }

            ElementTheme = theme;
            _ = _themeSelectorService.SetThemeAsync(theme);
        }
    }

    /// <summary>
    /// SelectedIndex of the Segmented control that exposes <see cref="BackdropType"/>.
    /// Round-trips Mica/MicaAlt/Acrylic/None to 0/1/2/3; out-of-range values are
    /// no-ops.
    /// </summary>
    public int BackdropTypeIndex
    {
        get => BackdropType switch
        {
            BackdropType.Mica => 0,
            BackdropType.MicaAlt => 1,
            BackdropType.Acrylic => 2,
            BackdropType.None => 3,
            _ => 0,
        };
        set
        {
            if (value < 0 || value > 3)
            {
                return;
            }

            var backdrop = value switch
            {
                0 => BackdropType.Mica,
                1 => BackdropType.MicaAlt,
                2 => BackdropType.Acrylic,
                _ => BackdropType.None,
            };

            if (backdrop == BackdropType)
            {
                return;
            }

            BackdropType = backdrop;
            _ = _backdropSelectorService.SetBackdropAsync(backdrop);
        }
    }

    /// <summary>
    /// SelectedIndex of the Segmented control that exposes
    /// <see cref="ScrobbleConditionMode"/>. Or → 0, And → 1.
    /// </summary>
    public int ScrobbleConditionModeIndex
    {
        get => ScrobbleConditionMode switch
        {
            ScrobbleConditionMode.Or => 0,
            ScrobbleConditionMode.And => 1,
            _ => 0,
        };
        set
        {
            if (value < 0 || value > 1)
            {
                return;
            }

            var mode = value == 0 ? ScrobbleConditionMode.Or : ScrobbleConditionMode.And;

            if (mode == ScrobbleConditionMode)
            {
                return;
            }

            ScrobbleConditionMode = mode;
            _ = _scrobblingService.SetConditionModeAsync(mode);
        }
    }

    public bool CanResetUiFonts => UiFontFamilies.Count > 0;
    public bool CanResetLyricsFonts => LyricsFontFamilies.Count > 0;
    public bool CanResetUiScale => Math.Abs(UiScale - DefaultUiScale) > 0.0001;

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

    public ICommand ResetUiFontsCommand { get; }
    public ICommand ResetLyricsFontsCommand { get; }
    public ICommand ResetUiScaleCommand { get; }
    public ICommand ClearImageCacheCommand { get; }

    public SettingsViewModel(
        IThemeSelectorService themeSelectorService,
        IBackdropSelectorService backdropSelectorService,
        IScrobblingService scrobblingService,
        ILocalSettingsService localSettingsService,
        IAppStateService appStateService,
        ITrayIconService trayIconService,
        IImageService imageService)
    {
        _themeSelectorService = themeSelectorService;
        _backdropSelectorService = backdropSelectorService;
        _scrobblingService = scrobblingService;
        _localSettingsService = localSettingsService;
        _appStateService = appStateService;
        _trayIconService = trayIconService;
        _imageService = imageService;
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

        _isInitializingGeneral = true;
        MinimizeToTray = _appStateService.State.General.MinimizeToTray;
        CloseToTray = _appStateService.State.General.CloseToTray;
        _isInitializingGeneral = false;

        // Default the UI scale to 100% before async load so the bound Segmented
        // control settles on the correct segment if the load is slow.
        UiScale = DefaultUiScale;

        UiFontFamilies.CollectionChanged += OnUiFontFamiliesChanged;
        LyricsFontFamilies.CollectionChanged += OnLyricsFontFamiliesChanged;

        ResetUiFontsCommand = new RelayCommand(
            () => UiFontFamilies.Clear(),
            () => CanResetUiFonts);

        ResetLyricsFontsCommand = new RelayCommand(
            () => LyricsFontFamilies.Clear(),
            () => CanResetLyricsFonts);

        ResetUiScaleCommand = new RelayCommand(
            () => UiScale = DefaultUiScale,
            () => CanResetUiScale);

        ClearImageCacheCommand = new AsyncRelayCommand(ClearImageCacheAsync);

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

        // Fire-and-forget load of the persisted font settings. The
        // _isInitializingFontSettings guard prevents the seeding writes from
        // round-tripping back through the persistence path.
        _ = InitializeFontSettingsAsync();
        _ = RefreshImageCacheStatsAsync();
    }

    private async Task RefreshImageCacheStatsAsync()
    {
        try
        {
            var stats = await _imageService.GetStatsAsync();
            ImageCacheUsageText = $"{FormatImageCount(stats.EntryCount)} \u00b7 {FormatByteSize(stats.TotalBytes)}";
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ImageCacheUsageText = "Unavailable";
        }
    }

    private async Task ClearImageCacheAsync()
    {
        IsImageCacheBusy = true;
        try
        {
            await _imageService.ClearAsync();
            await RefreshImageCacheStatsAsync();
        }
        finally
        {
            IsImageCacheBusy = false;
        }
    }

    private static string FormatImageCount(int count)
    {
        return count == 1 ? "1 image" : $"{count} images";
    }

    internal static string FormatByteSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }

    /// <summary>
    /// Loads the persisted font customization values into the observable
    /// collections and properties. Idempotent — repeated calls return the same
    /// in-flight or completed Task so callers (such as tests) can safely await
    /// the constructor's fire-and-forget invocation.
    /// </summary>
    internal Task InitializeFontSettingsAsync()
    {
        return _fontInitTask ??= InitializeFontSettingsCoreAsync();
    }

    private async Task InitializeFontSettingsCoreAsync()
    {
        try
        {
            var ui = await _localSettingsService.ReadSettingAsync<List<string>>(UiFontFamiliesKey);
            var lyrics = await _localSettingsService.ReadSettingAsync<List<string>>(LyricsFontFamiliesKey);
            var scale = await _localSettingsService.ReadSettingAsync<double?>(UiScaleKey);

            if (ui != null)
            {
                foreach (var name in GetPersistableFontFamilies(ui))
                {
                    UiFontFamilies.Add(name);
                }
            }

            if (lyrics != null)
            {
                foreach (var name in GetPersistableFontFamilies(lyrics))
                {
                    LyricsFontFamilies.Add(name);
                }
            }

            if (scale.HasValue)
            {
                UiScale = scale.Value;
            }
        }
        finally
        {
            _appliedUiFontFamilies = GetPersistableFontFamilies(UiFontFamilies);
            _appliedLyricsFontFamilies = GetPersistableFontFamilies(LyricsFontFamilies);
            _appliedUiScale = UiScale;
            IsFontRestartRequired = false;
            _isInitializingFontSettings = false;
            IsFontSettingsInitialized = true;

            // Surface the loaded state to the bound Reset buttons.
            OnPropertyChanged(nameof(CanResetUiFonts));
            OnPropertyChanged(nameof(CanResetLyricsFonts));
            OnPropertyChanged(nameof(CanResetUiScale));
            (ResetUiFontsCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (ResetLyricsFontsCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (ResetUiScaleCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    private void OnUiFontFamiliesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanResetUiFonts));
        (ResetUiFontsCommand as RelayCommand)?.NotifyCanExecuteChanged();

        if (_isInitializingFontSettings)
        {
            return;
        }

        UpdateFontRestartRequired();
        QueueFontSettingSave(UiFontFamiliesKey, GetPersistableFontFamilies(UiFontFamilies));
    }

    private void OnLyricsFontFamiliesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanResetLyricsFonts));
        (ResetLyricsFontsCommand as RelayCommand)?.NotifyCanExecuteChanged();

        if (_isInitializingFontSettings)
        {
            return;
        }

        UpdateFontRestartRequired();
        QueueFontSettingSave(LyricsFontFamiliesKey, GetPersistableFontFamilies(LyricsFontFamilies));
    }

    private void UpdateFontRestartRequired()
    {
        IsFontRestartRequired =
            !GetPersistableFontFamilies(UiFontFamilies).SequenceEqual(_appliedUiFontFamilies) ||
            !GetPersistableFontFamilies(LyricsFontFamilies).SequenceEqual(_appliedLyricsFontFamilies) ||
            Math.Abs(UiScale - _appliedUiScale) > 0.0001;
    }

    private static List<string> GetPersistableFontFamilies(IEnumerable<string> familyNames)
    {
        return familyNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .ToList();
    }

    partial void OnUiScaleChanged(double value)
    {
        (ResetUiScaleCommand as RelayCommand)?.NotifyCanExecuteChanged();

        if (_isInitializingFontSettings)
        {
            return;
        }

        UpdateFontRestartRequired();
        QueueFontSettingSave(UiScaleKey, value);
    }

    internal Task WaitForFontSettingsPersistenceAsync()
    {
        lock (_fontSettingsSaveLock)
        {
            return _fontSettingsSaveQueue;
        }
    }

    private void QueueFontSettingSave<T>(string key, T value)
    {
        lock (_fontSettingsSaveLock)
        {
            _fontSettingsSaveQueue = SaveFontSettingAfterPreviousAsync(_fontSettingsSaveQueue, key, value);
        }
    }

    private async Task SaveFontSettingAfterPreviousAsync<T>(Task previousSave, string key, T value)
    {
        try
        {
            await previousSave.ConfigureAwait(false);
        }
        catch
        {
            // Keep later preference writes moving if an earlier write fails.
        }

        await _localSettingsService.SaveSettingAsync(key, value).ConfigureAwait(false);
    }

    partial void OnIsScrobblingEnabledChanged(bool value)
    {
        if (!_isInitializingScrobbling)
            _ = _scrobblingService.SetEnabledAsync(value);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_isInitializingGeneral) return;
        _appStateService.State.General.MinimizeToTray = value;
        _ = _appStateService.SaveGeneralAsync();
        _trayIconService.RefreshVisibility();
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (_isInitializingGeneral) return;
        _appStateService.State.General.CloseToTray = value;
        _ = _appStateService.SaveGeneralAsync();
        _trayIconService.RefreshVisibility();
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
