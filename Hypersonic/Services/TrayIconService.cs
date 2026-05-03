using System.Runtime.InteropServices;

using CommunityToolkit.Mvvm.Input;

using H.NotifyIcon;
using H.NotifyIcon.Core;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Helpers;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Hypersonic.Services;

public sealed class TrayIconService : ITrayIconService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // Flip to false to fall back to the native Win32 popup (PopupMenu) if the
    // preview SecondWindow rendering misbehaves on a target. SecondWindow
    // hosts the MenuFlyout in a borderless WinUI window so it picks up Fluent
    // chrome (rounded corners, MenuFlyoutItem styling, theme).
    private const bool UseSecondWindow = true;

    private readonly IAppStateService _appStateService;
    private readonly ILogger _log;

    private DispatcherQueue? _dispatcherQueue;
    private bool _initialized;
    private TaskbarIcon? _icon;

    public TrayIconService(
        IAppStateService appStateService,
        ILoggingService logService)
    {
        _appStateService = appStateService;
        _log = logService.GetLogger(nameof(TrayIconService));
    }

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue == null)
        {
            throw new InvalidOperationException(
                "TrayIconService.Initialize() must be called on the UI thread.");
        }

        _dispatcherQueue = queue;
        _initialized = true;
        _log.Debug("Initialized");
    }

    public void RefreshVisibility()
    {
        if (!_initialized) return;
        EnsureUiThread();

        var enabled = _appStateService.State.General.MinimizeToTray
                   || _appStateService.State.General.CloseToTray;
        if (enabled && _icon == null) CreateIcon();
        else if (!enabled && _icon != null) DisposeIcon();
    }

    public void RequestExit()
    {
        EnsureUiThread();
        App.IsExiting = true;
        DisposeIcon();
        App.MainWindow.Close();
    }

    public void ShowMainWindow()
    {
        EnsureUiThread();

        var window = App.MainWindow;
        window.Show(disableEfficiencyMode: true);

        if (window.AppWindow.Presenter is OverlappedPresenter op &&
            op.State == OverlappedPresenterState.Minimized)
        {
            op.Restore();
        }

        window.BringToFront();
        window.Activate();
    }

    public void Dispose()
    {
        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
        {
            DisposeIcon();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(DisposeIcon);
        }
    }

    private void CreateIcon()
    {
        // Capture whether MainWindow is the foreground HWND right now, not
        // merely visible. The tray-icon recreate path below (PrepareContext-
        // MenuWindow's warm-up ShowAt) will steal focus regardless of who
        // currently holds it; reactivating MainWindow at the end is only
        // correct when MainWindow was the user's actual focus before we
        // started. Using window.Visible would also reactivate when the user
        // had Hypersonic open in the background but was working in another
        // app -- yanking focus away from them.
        var window = App.MainWindow;
        var mainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var hadFocus = mainHwnd != IntPtr.Zero && GetForegroundWindow() == mainHwnd;

        // ---------------------------------------------------------------
        // First-open menu sizing notes (H.NotifyIcon SecondWindow mode):
        //
        // H.NotifyIcon.WinUI 2.4.1's SecondWindow code path (in
        // TaskbarIcon.ContextMenu.WinUI.SecondWindow.cs) does NOT show our
        // MenuFlyout directly. It MOVES our items into a brand-new MenuFlyout
        // it constructs internally, sizes the host window from a manual
        // item.Measure() pass, then ShowAt's that internal flyout in a bare
        // Frame-hosted Window.
        //
        // Two consequences for us:
        //
        //  1. Any MenuFlyoutPresenterStyle we set on the MenuFlyout below is
        //     DISCARDED (it lived on our flyout, not the library's internal
        //     one). The fix is the implicit Style for MenuFlyoutPresenter in
        //     App.xaml -- implicit app-level styles cascade into the library's
        //     internal flyout too. That style disables both ScrollViewers in
        //     the presenter template, killing the spurious scrollbar.
        //
        //  2. The library FORCE-SETS Height = 32 on each item it moves in.
        //     We must match that here (not 36, not "no height"), because:
        //       - On the FIRST right-click, the library calls item.Measure()
        //         BEFORE its Height=32 override has been applied to a real
        //         arrange pass, so DesiredSize reflects whatever Height we
        //         set. If our value disagrees with 32, the host window is
        //         sized for one height and the rendered presenter uses
        //         another, causing the inner ScrollViewer to engage.
        //       - On the SECOND open, the items have been arranged once at
        //         Height=32, so DesiredSize matches and the window fits.
        //     Using Height = 32 here makes both opens consistent.
        // ---------------------------------------------------------------
        var showItem = new MenuFlyoutItem
        {
            Text = "TrayIcon_Show".GetLocalized(),
            Icon = new FontIcon { Glyph = "\uE8A7" }, // OpenInNewWindow
            Command = new RelayCommand(ShowMainWindow),
            MinWidth = 200,
            Height = 32,
        };
        var exitItem = new MenuFlyoutItem
        {
            Text = "TrayIcon_Exit".GetLocalized(),
            Icon = new FontIcon { Glyph = "\uE7E8" }, // ChromeClose
            Command = new RelayCommand(RequestExit),
            MinWidth = 200,
            Height = 32,
        };
        var menu = new MenuFlyout();
        menu.Items.Add(showItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exitItem);

        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/WindowIcon.ico")),
            ToolTipText = "AppDisplayName".GetLocalized(),
            ContextMenuMode = UseSecondWindow ? ContextMenuMode.SecondWindow : ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            LeftClickCommand = new RelayCommand(ShowMainWindow),
            DoubleClickCommand = new RelayCommand(ShowMainWindow),
        };

        // Note: we deliberately do NOT apply the app's theme to the tray
        // icon. System tray menus should follow the Windows app theme
        // (system Light/Dark setting), not an in-app override -- otherwise
        // a Hypersonic-Dark menu sits next to system-Light tray icons and
        // looks out of place. With no RequestedTheme override, the
        // SecondWindow host (whose owner HWND is H.NotifyIcon's hidden
        // message window, not MainWindow) has no parent theme to inherit
        // from and falls back to the system theme, which is correct.

        // Disable Efficiency Mode so background audio playback stays smooth.
        _icon.ForceCreate(enablesEfficiencyMode: false);

        // ---------------------------------------------------------------
        // Focus flicker on enable (accepted limitation):
        //
        // When the user toggles MinimizeToTray/CloseToTray on, MainWindow
        // briefly loses foreground focus and visibly flickers before we
        // reactivate it below.
        //
        // Root cause is in H.NotifyIcon, not Shell_NotifyIcon:
        // TaskbarIcon.ContextMenu.WinUI.SecondWindow.PrepareContextMenuWindow
        // runs synchronously when ContextMenuMode = SecondWindow is applied
        // to the new TaskbarIcon above. It:
        //   1. Constructs a real top-level Microsoft.UI.Xaml.Window for the
        //      menu host (no WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW), owned by
        //      the 0x0 hidden MessageWindow rather than MainWindow -- so
        //      Windows has no reason to keep MainWindow in the foreground;
        //   2. Inside that window's frame.Loaded handler, momentarily calls
        //      flyout.ShowAt(...).Hide() to "warm up" the flyout. That
        //      ShowAt forces an activation cycle on the new top-level HWND.
        //
        // No public knob disables either step. Workarounds we considered:
        //   - ContextMenuMode.PopupMenu: avoids the second window entirely,
        //     but loses the WinUI-styled context menu we want.
        //   - Lazy-upgrade to SecondWindow on first right-click: hides the
        //     flicker behind a context where the user already shifted
        //     attention to the tray, but adds timing complexity and a race
        //     against the library's right-click handling.
        //   - Upstream patch (set NOACTIVATE+TOOLWINDOW on the host HWND
        //     before warm-up, and skip the warm-up): correct fix, but out
        //     of band -- requires PR + release.
        //
        // Decision: accept the one-time flicker on enable. It only happens
        // when the toggle flips false -> true (CreateIcon), not on every
        // right-click, not at startup if both toggles are off, not when
        // disabled. The deferred Activate below masks it as best we can.
        // ---------------------------------------------------------------
        // Restore focus to the main window. Deferred to the next dispatcher
        // tick because the activation cycle from the SecondWindow warm-up
        // happens after ForceCreate returns synchronously; activating
        // inline would race with that cycle and lose.
        if (hadFocus)
        {
            _dispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                window.Activate();
            });
        }

        _log.Debug("Tray icon created");
    }

    private void DisposeIcon()
    {
        _icon?.Dispose();
        _icon = null;
    }

    private void EnsureUiThread()
    {
        if (_dispatcherQueue == null || !_dispatcherQueue.HasThreadAccess)
        {
            throw new InvalidOperationException(
                "TrayIconService method called off the UI thread.");
        }
    }
}
