using Hypersonic.Activation;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;

namespace Hypersonic.Services;

public class ActivationService : IActivationService
{
    private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
    private readonly IEnumerable<IActivationHandler> _activationHandlers;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly IBackdropSelectorService _backdropSelectorService;
    private readonly ICredentialService _credentialService;
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly IAppStateService _appStateService;
    private readonly ILogger _log;

    public ActivationService(
        ActivationHandler<LaunchActivatedEventArgs> defaultHandler,
        IEnumerable<IActivationHandler> activationHandlers,
        IThemeSelectorService themeSelectorService,
        IBackdropSelectorService backdropSelectorService,
        ICredentialService credentialService,
        ISubsonicClientService subsonicClientService,
        IAppStateService appStateService,
        ILoggingService logService)
    {
        _defaultHandler = defaultHandler;
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
        _backdropSelectorService = backdropSelectorService;
        _credentialService = credentialService;
        _subsonicClientService = subsonicClientService;
        _appStateService = appStateService;
        _log = logService.GetLogger(nameof(ActivationService));
    }

    public async Task ActivateAsync(object activationArgs)
    {
        _log.Info("Activation started");

        // Execute tasks before activation.
        await InitializeAsync();

        // Initialize scrobbling after app state is loaded.
        // Must run on the UI thread (not inside InitializeAsync which
        // uses ConfigureAwait(false) and may resume on a thread pool thread).
        await App.GetService<IScrobblingService>().InitializeAsync();

        // Restore window geometry from saved state.
        RestoreWindowState();

        // Check credentials and set appropriate content.
        if (App.MainWindow.Content == null)
        {
            var credential = await _credentialService.GetActiveCredentialAsync();

            if (credential != null && !string.IsNullOrWhiteSpace(credential.ServerUrl))
            {
                _log.Info("Saved credential found", new { credential.ServerUrl, credential.Username });

                // Try saved credentials
                try
                {
                    var result = await _subsonicClientService.TestConnectionAsync(credential);

                    if (result.Response.Status == "ok")
                    {
                        credential.ServerUrl = result.ResolvedUrl;
                        _subsonicClientService.CreateClient(credential);
                        _log.Info("Auto-connect succeeded, showing shell");
                        ShowShell(activationArgs);
                    }
                    else
                    {
                        var errorDetail = result.Response.Error?.Message ?? "Server returned an error";
                        _log.Warning("Auto-connect returned error", new { Error = errorDetail });
                        ShowConnectPage(errorDetail);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning("Auto-connect failed", exception: ex);
                    ShowConnectPage($"Could not connect: {ex.Message}");
                }
            }
            else
            {
                _log.Info("No saved credential, showing connect page");
                ShowConnectPage();
            }
        }

        // Activate the MainWindow.
        App.MainWindow.Activate();

        // Initialize taskbar integration after window is activated.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        App.GetService<ITaskbarService>().Initialize(hwnd);

        // Execute tasks after activation.
        await StartupAsync();

        _log.Info("Activation completed");
    }

    private void ShowShell(object activationArgs)
    {
        var shell = App.GetService<ShellPage>();
        App.MainWindow.Content = shell;

        // Handle activation via ActivationHandlers (navigates to MainPage).
        HandleActivationAsync(activationArgs).ConfigureAwait(false);
    }

    private static void ShowConnectPage(string? errorMessage = null)
    {
        var connectPage = App.GetService<ConnectPage>();
        App.MainWindow.Content = connectPage;

        if (errorMessage != null)
        {
            connectPage.ViewModel.InvalidCredentialMessage = errorMessage;
            connectPage.ViewModel.IsInvalidCredentialVisible = true;
        }
    }

    private async Task HandleActivationAsync(object activationArgs)
    {
        var activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));

        if (activationHandler != null)
        {
            await activationHandler.HandleAsync(activationArgs);
        }

        if (_defaultHandler.CanHandle(activationArgs))
        {
            await _defaultHandler.HandleAsync(activationArgs);
        }
    }

    private async Task InitializeAsync()
    {
        _log.Debug("Initializing theme and backdrop");
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        await _backdropSelectorService.InitializeAsync().ConfigureAwait(false);

        // Initialize app state (load AppState.json).
        await _appStateService.InitializeAsync();

        // Migrate legacy credentials if needed.
        await _credentialService.MigrateFromLegacyAsync();
    }

    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();
        await _backdropSelectorService.SetRequestedBackdropAsync();
        await Task.CompletedTask;
    }

    private void RestoreWindowState()
    {
        var windowState = _appStateService.State.Window;
        var window = App.MainWindow;

        // Always apply saved normal bounds first, so the window's restore-size
        // is correct regardless of whether we then maximize.
        if (windowState.Width.HasValue && windowState.Height.HasValue)
        {
            window.Width = windowState.Width.Value;
            window.Height = windowState.Height.Value;
        }

        if (windowState.X.HasValue && windowState.Y.HasValue)
        {
            if (IsPositionOnScreen(windowState.X.Value, windowState.Y.Value))
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(windowState.X.Value, windowState.Y.Value));
                _log.Debug("Window position restored", new { windowState.X, windowState.Y });
            }
            else
            {
                _log.Debug("Saved window position is off-screen, using OS default");
            }
        }

        if (windowState.IsMaximized)
        {
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
            _log.Debug("Window restored to maximized state");
        }
    }

    private static bool IsPositionOnScreen(int x, int y)
    {
        try
        {
            var areas = DisplayArea.FindAll();
            foreach (var area in areas)
            {
                var bounds = area.WorkArea;
                if (x >= bounds.X && x < bounds.X + bounds.Width &&
                    y >= bounds.Y && y < bounds.Y + bounds.Height)
                {
                    return true;
                }
            }
        }
        catch
        {
            // If we can't enumerate displays, allow the position.
            return true;
        }

        return false;
    }
}
