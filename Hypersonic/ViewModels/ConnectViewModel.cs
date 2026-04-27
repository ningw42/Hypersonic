using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.ViewModels;
using Hypersonic.Views;

using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.ViewModels;

public partial class ConnectViewModel : ObservableRecipient
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ICredentialService _credentialService;
    private readonly IAppStateService _appStateService;
    private readonly ILogger _log;

    [ObservableProperty]
    public partial string ServerUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsTestingConnection { get; set; }

    [ObservableProperty]
    public partial bool IsTestResultVisible { get; set; }

    [ObservableProperty]
    public partial string TestResultMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity TestResultSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsInvalidCredentialVisible { get; set; }

    [ObservableProperty]
    public partial string InvalidCredentialMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsConnecting { get; set; }

    private static readonly HashSet<string> TopLevelPages = new()
    {
        typeof(MainViewModel).FullName!,
        typeof(AlbumsViewModel).FullName!,
        typeof(ArtistsViewModel).FullName!,
        typeof(TracksViewModel).FullName!,
        typeof(PlaylistsViewModel).FullName!,
        typeof(SettingsViewModel).FullName!,
    };

    public ConnectViewModel(
        ISubsonicClientService subsonicClientService,
        ICredentialService credentialService,
        IAppStateService appStateService,
        ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _credentialService = credentialService;
        _appStateService = appStateService;
        _log = logService.GetLogger(nameof(ConnectViewModel));
    }

    public async Task LoadSavedCredentialsAsync()
    {
        // Always try to prefill URL + username from settings.
        var account = await _credentialService.GetActiveServerAccountAsync();
        if (account != null)
        {
            ServerUrl = account.ServerUrl;
            Username = account.Username;
        }

        // Try to get password from vault; if unavailable, leave empty.
        var credential = await _credentialService.GetActiveCredentialAsync();
        if (credential != null)
        {
            Password = credential.Password;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            TestResultMessage = "Please fill in all fields.";
            TestResultSeverity = InfoBarSeverity.Warning;
            IsTestResultVisible = true;
            return;
        }

        IsTestingConnection = true;
        IsTestResultVisible = false;

        try
        {
            var credential = new ServerCredential
            {
                ServerUrl = ServerUrl,
                Username = Username,
                Password = Password,
            };

            var result = await _subsonicClientService.TestConnectionAsync(credential);

            if (result.Response.Status == "ok")
            {
                var serverInfo = !string.IsNullOrEmpty(result.Response.Type)
                    ? $"{result.Response.Type} {result.Response.ServerVersion}"
                    : $"Subsonic API {result.Response.Version}";
                TestResultMessage = $"Connected successfully to {serverInfo}";
                TestResultSeverity = InfoBarSeverity.Success;
                _log.Info("Test connection succeeded", new { ServerUrl = ServerUrl, ServerInfo = serverInfo });
            }
            else
            {
                var errorDetail = result.Response.Error?.Message ?? "Unknown error";
                TestResultMessage = $"Server returned error: {errorDetail}";
                TestResultSeverity = InfoBarSeverity.Error;
                _log.Warning("Test connection returned error", new { ServerUrl = ServerUrl, Error = errorDetail });
            }
        }
        catch (HttpRequestException ex)
        {
            TestResultMessage = $"Connection failed: {ex.Message}";
            TestResultSeverity = InfoBarSeverity.Error;
            _log.Warning("Test connection failed", new { ServerUrl = ServerUrl }, ex);
        }
        catch (Exception ex)
        {
            TestResultMessage = $"Unexpected error: {ex.Message}";
            TestResultSeverity = InfoBarSeverity.Error;
            _log.Error("Test connection unexpected error", new { ServerUrl = ServerUrl }, ex);
        }
        finally
        {
            IsTestingConnection = false;
            IsTestResultVisible = true;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            TestResultMessage = "Please fill in all fields.";
            TestResultSeverity = InfoBarSeverity.Warning;
            IsTestResultVisible = true;
            return;
        }

        IsConnecting = true;

        try
        {
            var credential = new ServerCredential
            {
                ServerUrl = ServerUrl,
                Username = Username,
                Password = Password,
            };

            // Test first
            var result = await _subsonicClientService.TestConnectionAsync(credential);

            if (result.Response.Status != "ok")
            {
                var errorDetail = result.Response.Error?.Message ?? "Unknown error";
                TestResultMessage = $"Connection failed: {errorDetail}";
                TestResultSeverity = InfoBarSeverity.Error;
                IsTestResultVisible = true;
                return;
            }

            // Save credentials with the resolved URL and create the active client
            credential.ServerUrl = result.ResolvedUrl;
            await _credentialService.SaveCredentialAsync(credential);
            await _credentialService.SetActiveServerAsync(result.ResolvedUrl, credential.Username);
            _subsonicClientService.CreateClient(credential);
            _log.Info("Connected and credentials saved", new { ResolvedUrl = result.ResolvedUrl, credential.Username });

            // Swap to the shell with navigation
            var shell = App.GetService<ShellPage>();
            App.MainWindow.Content = shell;
            var navigationService = App.GetService<INavigationService>();

            // Navigate to last page if it was a top-level page, otherwise default to MainViewModel.
            var lastPage = _appStateService.State.Navigation.LastPage;
            var pageKey = lastPage != null && TopLevelPages.Contains(lastPage)
                ? lastPage
                : typeof(MainViewModel).FullName!;
            navigationService.NavigateTo(pageKey);
        }
        catch (HttpRequestException ex)
        {
            TestResultMessage = $"Connection failed: {ex.Message}";
            TestResultSeverity = InfoBarSeverity.Error;
            IsTestResultVisible = true;
            _log.Warning("Connect failed", new { ServerUrl = ServerUrl }, ex);
        }
        catch (Exception ex)
        {
            TestResultMessage = $"Unexpected error: {ex.Message}";
            TestResultSeverity = InfoBarSeverity.Error;
            IsTestResultVisible = true;
            _log.Error("Connect unexpected error", new { ServerUrl = ServerUrl }, ex);
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
