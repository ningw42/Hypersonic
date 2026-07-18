using H.NotifyIcon;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Services;
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

    public MainWindow()
    {
        InitializeComponent();
        PersistenceId = AppStateWindowPersistenceStorage.PersistenceId;

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
        AppWindow.Changed += AppWindow_Changed;
        AppWindow.Closing += AppWindow_Closing;

        // Dedicated window-activation hook for favorites polling — separate from ShellPage's
        // transient titlebar Activated handler, so a recreated shell can't leak a stale
        // subscription on this single long-lived window.
        Activated += MainWindow_Activated;
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
        }
        catch
        {
            // Service not yet available during early window construction.
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Hide-to-tray when the user minimizes (only if the toggle is on).
        if (args.DidPresenterChange &&
            AppWindow.Presenter is OverlappedPresenter op &&
            op.State == OverlappedPresenterState.Minimized)
        {
            EnsureAppStateService();
            if (_appStateService?.State.General.MinimizeToTray == true)
            {
                this.Hide(enableEfficiencyMode: false);
            }
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Real exit was requested from the tray menu - let it proceed.
        if (App.IsExiting)
        {
            return;
        }

        EnsureAppStateService();
        if (_appStateService?.State.General.CloseToTray == true)
        {
            args.Cancel = true;
            this.Hide(enableEfficiencyMode: false);
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        // Re-poll starred state on window activation to catch external stars (Navidrome web,
        // another device). Resolve lazily: App.MainWindow is constructed before the DI host,
        // so a constructor-time resolve would be premature. No-ops cleanly if disconnected.
        try
        {
            _ = App.GetService<IFavoritesService>().RefreshAsync();
        }
        catch
        {
            // DI host / service not available during very early activation.
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
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

        // Dispose taskbar service before playback: it unhooks the comctl32
        // window subclass (whose managed WndProc delegate must not be called
        // after teardown) and unsubscribes from playback events.
        try
        {
            App.GetService<ITaskbarService>().Dispose();
        }
        catch (Exception ex)
        {
            try
            {
                App.GetService<ILoggingService>()
                    .GetLogger(nameof(MainWindow))
                    .Warning("Taskbar service dispose failed during shutdown", exception: ex);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Taskbar service dispose failed during shutdown: {ex}");
            }
        }

        try
        {
            if (App.GetService<IPlaybackService>() is IDisposable disposablePlaybackService)
            {
                disposablePlaybackService.Dispose();
            }
        }
        catch (Exception ex)
        {
            try
            {
                App.GetService<ILoggingService>()
                    .GetLogger(nameof(MainWindow))
                    .Warning("Playback service dispose failed during shutdown", exception: ex);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Playback service dispose failed during shutdown: {ex}");
            }
        }

        try
        {
            if (App.GetService<IImageService>() is IDisposable disposableImageService)
            {
                disposableImageService.Dispose();
            }
        }
        catch (Exception ex)
        {
            try
            {
                App.GetService<ILoggingService>()
                    .GetLogger(nameof(MainWindow))
                    .Warning("Image service dispose failed during shutdown", exception: ex);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Image service dispose failed during shutdown: {ex}");
            }
        }

        try
        {
            if (App.GetService<IImageCacheStore>() is IDisposable disposableStore)
            {
                disposableStore.Dispose();
            }
            else
            {
                App.GetService<IImageCacheStore>().FlushAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            try
            {
                App.GetService<ILoggingService>()
                    .GetLogger(nameof(MainWindow))
                    .Warning("Image cache manifest flush failed during shutdown", exception: ex);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Image cache manifest flush failed during shutdown: {ex}");
            }
        }

        try
        {
            App.GetService<ITrayIconService>().Dispose();
        }
        catch (Exception ex)
        {
            try
            {
                App.GetService<ILoggingService>()
                    .GetLogger(nameof(MainWindow))
                    .Warning("Tray icon service dispose failed during shutdown", exception: ex);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Tray icon service dispose failed during shutdown: {ex}");
            }
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
