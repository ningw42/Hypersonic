using H.NotifyIcon;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Services;
using Hypersonic.Helpers;
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
        AppWindow.Closing += AppWindow_Closing;
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
            _windowSaveTimer.Tick += (_, _) => WriteWindowState(sync: false);
        }
        catch
        {
            // Service not yet available during early window construction.
        }
    }

    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        EnsureAppStateService();
        CaptureWindowState();
        DebounceSaveWindow();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange || args.DidPresenterChange)
        {
            EnsureAppStateService();
            CaptureWindowState();
            DebounceSaveWindow();
        }

        // Hide-to-tray when the user minimizes (only if the toggle is on).
        if (args.DidPresenterChange &&
            AppWindow.Presenter is OverlappedPresenter op &&
            op.State == OverlappedPresenterState.Minimized &&
            _appStateService?.State.General.MinimizeToTray == true)
        {
            // Flush synchronously before hiding. CaptureWindowState() above
            // was a no-op for this transition (presenter is now Minimized so
            // we skip overwriting the model with iconic geometry), but the
            // model already holds the *pre-minimize* values from the last
            // size/position change. Persist those now so a process kill
            // while hidden doesn't lose them.
            _windowSaveTimer?.Stop();
            WriteWindowState(sync: true);

            this.Hide(enableEfficiencyMode: false);
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Real exit was requested from the tray menu - let it proceed.
        if (App.IsExiting) return;

        EnsureAppStateService();
        if (_appStateService?.State.General.CloseToTray == true)
        {
            args.Cancel = true;

            // Flush window state synchronously before hiding, mirroring the
            // sync save in MainWindow_Closed. Without this, a resize or move
            // made within ~1s of clicking X would only be persisted by the
            // pending debounce timer firing after Hide() -- and that write
            // can be lost if the process is terminated (task manager kill,
            // OS shutdown, crash) before the user picks Exit-from-tray.
            _windowSaveTimer?.Stop();
            CaptureWindowState();
            WriteWindowState(sync: true);

            this.Hide(enableEfficiencyMode: false);
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

    /// <summary>
    /// Updates the in-memory window-state model from the live window
    /// properties. Skipped while the presenter is Minimized so iconic
    /// bounds never clobber the user's real geometry. Cheap; safe to
    /// call from every size/position event.
    /// </summary>
    private void CaptureWindowState()
    {
        if (_appStateService == null)
        {
            return;
        }

        if (AppWindow.Presenter is OverlappedPresenter mp &&
            mp.State == OverlappedPresenterState.Minimized)
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
    }

    /// <summary>
    /// Persists whatever the window-state model currently holds. Does NOT
    /// re-read the live window -- callers should call CaptureWindowState()
    /// first if they want fresh values. Splitting capture from write lets
    /// the timer-deferred path persist correctly even if the timer fires
    /// after the window has already minimized: the model already holds
    /// pre-minimize values, and writing them is the right behavior.
    /// </summary>
    private void WriteWindowState(bool sync)
    {
        if (_appStateService == null)
        {
            return;
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

        // Flush window state synchronously on close. Capture once first
        // (no-op if minimized -- model retains good values from the last
        // pre-minimize change).
        CaptureWindowState();
        WriteWindowState(sync: true);

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

        try { App.GetService<ITrayIconService>().Dispose(); } catch { /* may not be available */ }
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
