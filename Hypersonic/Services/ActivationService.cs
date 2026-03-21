using Hypersonic.Activation;
using Hypersonic.Contracts.Services;
using Hypersonic.Models;
using Hypersonic.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Services;

public class ActivationService : IActivationService
{
    private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
    private readonly IEnumerable<IActivationHandler> _activationHandlers;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ISubsonicClientService _subsonicClientService;

    public ActivationService(
        ActivationHandler<LaunchActivatedEventArgs> defaultHandler,
        IEnumerable<IActivationHandler> activationHandlers,
        IThemeSelectorService themeSelectorService,
        ILocalSettingsService localSettingsService,
        ISubsonicClientService subsonicClientService)
    {
        _defaultHandler = defaultHandler;
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
        _subsonicClientService = subsonicClientService;
    }

    public async Task ActivateAsync(object activationArgs)
    {
        // Execute tasks before activation.
        await InitializeAsync();

        // Check credentials and set appropriate content.
        if (App.MainWindow.Content == null)
        {
            var credential = await _localSettingsService.ReadSettingAsync<ServerCredential>("ServerCredential");

            if (credential != null && !string.IsNullOrWhiteSpace(credential.ServerUrl))
            {
                // Try saved credentials
                try
                {
                    var result = await _subsonicClientService.TestConnectionAsync(credential);

                    if (result.Response.Status == "ok")
                    {
                        credential.ServerUrl = result.ResolvedUrl;
                        _subsonicClientService.CreateClient(credential);
                        ShowShell(activationArgs);
                    }
                    else
                    {
                        var errorDetail = result.Response.Error?.Message ?? "Server returned an error";
                        ShowConnectPage(errorDetail);
                    }
                }
                catch (Exception ex)
                {
                    ShowConnectPage($"Could not connect: {ex.Message}");
                }
            }
            else
            {
                ShowConnectPage();
            }
        }

        // Activate the MainWindow.
        App.MainWindow.Activate();

        // Execute tasks after activation.
        await StartupAsync();
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
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        await Task.CompletedTask;
    }

    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();
        await Task.CompletedTask;
    }
}
