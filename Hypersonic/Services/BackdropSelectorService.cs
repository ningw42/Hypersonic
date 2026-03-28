using Hypersonic.Contracts.Services;
using Hypersonic.Models;

using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Hypersonic.Services;

public class BackdropSelectorService : IBackdropSelectorService
{
    private const string SettingsKey = "AppRequestedBackdrop";

    public BackdropType Backdrop { get; set; } = BackdropType.Mica;

    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _log;

    public BackdropSelectorService(ILocalSettingsService localSettingsService, ILoggingService logService)
    {
        _localSettingsService = localSettingsService;
        _log = logService.GetLogger(nameof(BackdropSelectorService));
    }

    public async Task InitializeAsync()
    {
        Backdrop = await LoadBackdropFromSettingsAsync();
        _log.Info("Backdrop loaded", new { Backdrop });
        await Task.CompletedTask;
    }

    public async Task SetBackdropAsync(BackdropType backdrop)
    {
        Backdrop = backdrop;

        await SetRequestedBackdropAsync();
        await SaveBackdropInSettingsAsync(Backdrop);
    }

    public async Task SetRequestedBackdropAsync()
    {
        // NOTE: Only applies to MainWindow. Queue and search popups use Border backgrounds
        // (SolidBackgroundFillColorBaseBrush) and don't support SystemBackdrop.
        // Applying acrylic to popups would require a Composition API approach or
        // windowed popups — deferred to a future iteration.
        if (Backdrop == BackdropType.None)
        {
            App.MainWindow.SystemBackdrop = null;

            // With no backdrop, the window surface is unpainted (white by default).
            // Set a solid background matching the current theme. We use explicit colors
            // rather than a theme resource lookup because Application.Current.Resources
            // does not respect the element-level RequestedTheme override.
            if (App.MainWindow.Content is Page rootPage)
            {
                var isDark = rootPage.ActualTheme == ElementTheme.Dark;
                rootPage.Background = new SolidColorBrush(isDark ? Colors.Black : Colors.White);
            }
        }
        else
        {
            App.MainWindow.SystemBackdrop = Backdrop switch
            {
                BackdropType.Mica => new MicaBackdrop(),
                BackdropType.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                BackdropType.Acrylic => new DesktopAcrylicBackdrop(),
                _ => new MicaBackdrop(),
            };

            // Clear any solid background so the material backdrop shows through.
            if (App.MainWindow.Content is Page rootPage)
            {
                rootPage.Background = null;
            }
        }

        await Task.CompletedTask;
    }

    private async Task<BackdropType> LoadBackdropFromSettingsAsync()
    {
        var backdropName = await _localSettingsService.ReadSettingAsync<string>(SettingsKey);

        if (Enum.TryParse(backdropName, out BackdropType cachedBackdrop))
        {
            return cachedBackdrop;
        }

        return BackdropType.Mica;
    }

    private async Task SaveBackdropInSettingsAsync(BackdropType backdrop)
    {
        await _localSettingsService.SaveSettingAsync(SettingsKey, backdrop.ToString());
    }
}
