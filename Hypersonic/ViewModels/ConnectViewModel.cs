using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Models;
using Hypersonic.ViewModels;
using Hypersonic.Views;

using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.ViewModels;

public partial class ConnectViewModel : ObservableRecipient
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _log;

    [ObservableProperty]
    private string serverUrl = string.Empty;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool isTestingConnection;

    [ObservableProperty]
    private bool isTestResultVisible;

    [ObservableProperty]
    private string testResultMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity testResultSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private bool isInvalidCredentialVisible;

    [ObservableProperty]
    private string invalidCredentialMessage = string.Empty;

    [ObservableProperty]
    private bool isConnecting;

    public ConnectViewModel(
        ISubsonicClientService subsonicClientService,
        ILocalSettingsService localSettingsService,
        ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _localSettingsService = localSettingsService;
        _log = logService.GetLogger(nameof(ConnectViewModel));
    }

    public async Task LoadSavedCredentialsAsync()
    {
        var credential = await _localSettingsService.ReadSettingAsync<ServerCredential>("ServerCredential");
        if (credential != null)
        {
            ServerUrl = credential.ServerUrl;
            Username = credential.Username;
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
            await _localSettingsService.SaveSettingAsync("ServerCredential", credential);
            _subsonicClientService.CreateClient(credential);
            _log.Info("Connected and credentials saved", new { ResolvedUrl = result.ResolvedUrl, credential.Username });

            // Swap to the shell with navigation
            var shell = App.GetService<ShellPage>();
            App.MainWindow.Content = shell;
            var navigationService = App.GetService<INavigationService>();
            navigationService.NavigateTo(typeof(MainViewModel).FullName!);
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
