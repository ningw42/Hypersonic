using Hypersonic.Contracts.Services;
using Hypersonic.Helpers;
using Hypersonic.Services;
using Hypersonic.ViewModels;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

using Windows.UI.ViewManagement;

namespace Hypersonic;

public sealed partial class MainWindow : WindowEx
{
    private DispatcherQueue dispatcherQueue;

    private UISettings settings;

    private IAppStateService? _appStateService;
    private DispatcherQueueTimer? _windowSaveTimer;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Width = 1400;
        Height = 1000;
        Content = null;
        Title = "AppDisplayName".GetLocalized();

        // Theme change code picked from https://github.com/microsoft/WinUI-Gallery/pull/1239
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged; // cannot use FrameworkElement.ActualThemeChanged event

        Closed += MainWindow_Closed;
        SizeChanged += MainWindow_SizeChanged;
        AppWindow.Changed += AppWindow_Changed;
    }

    private void EnsureAppStateService()
    {
        if (_appStateService != null)
        {
            return;
        }

        try
        {
            _appStateService = App.GetService<IAppStateService>();
            _windowSaveTimer = dispatcherQueue.CreateTimer();
            _windowSaveTimer.Interval = TimeSpan.FromSeconds(1);
            _windowSaveTimer.IsRepeating = false;
            _windowSaveTimer.Tick += (_, _) => SaveWindowState();
        }
        catch
        {
            // Service not yet available during early window construction.
        }
    }

    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        EnsureAppStateService();
        DebounceSaveWindow();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange || args.DidPresenterChange)
        {
            EnsureAppStateService();
            DebounceSaveWindow();
        }
    }

    private void DebounceSaveWindow()
    {
        if (_isClosing || _appStateService == null || _windowSaveTimer == null)
        {
            return;
        }

        _windowSaveTimer.Stop();
        _windowSaveTimer.Start();
    }

    private void SaveWindowState(bool sync = false)
    {
        if (_appStateService == null)
        {
            return;
        }

        var windowState = _appStateService.State.Window;
        var isMaximized = AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Maximized;

        windowState.IsMaximized = isMaximized;

        if (!isMaximized)
        {
            windowState.Width = (int)Width;
            windowState.Height = (int)Height;
            var pos = AppWindow.Position;
            windowState.X = pos.X;
            windowState.Y = pos.Y;
        }

        if (sync)
        {
            _appStateService.SaveSync();
        }
        else
        {
            _ = _appStateService.SaveWindowAsync();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _isClosing = true;
        _windowSaveTimer?.Stop();

        // Flush window state synchronously on close.
        SaveWindowState(sync: true);

        // Flush pending playback saves.
        try
        {
            var player = App.GetService<PlayerViewModel>();
            player.FlushPendingSaves();
        }
        catch
        {
            // PlayerViewModel may not be available.
        }

        if (App.GetService<ILoggingService>() is LoggingService loggingService)
        {
            loggingService.Dispose();
        }
    }

    // this handles updating the caption button colors correctly when indows system theme is changed
    // while the app is open
    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        // This calls comes off-thread, hence we will need to dispatch it to current app's thread
        dispatcherQueue.TryEnqueue(() =>
        {
            TitleBarHelper.ApplySystemThemeToCaptionButtons();
            App.GetService<ITaskbarService>().OnSystemThemeChanged();
        });
    }
}
