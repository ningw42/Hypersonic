using Hypersonic.Contracts.Services;
using Hypersonic.Helpers;

using Microsoft.UI.Xaml;

namespace Hypersonic.Services;

public class ThemeSelectorService : IThemeSelectorService
{
    private const string SettingsKey = "AppBackgroundRequestedTheme";

    public ElementTheme Theme { get; set; } = ElementTheme.Default;

    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _log;

    public ThemeSelectorService(ILocalSettingsService localSettingsService, ILoggingService logService)
    {
        _localSettingsService = localSettingsService;
        _log = logService.GetLogger(nameof(ThemeSelectorService));
    }

    public async Task InitializeAsync()
    {
        Theme = await LoadThemeFromSettingsAsync();
        _log.Info("Theme loaded", new { Theme });
        await Task.CompletedTask;
    }

    public async Task SetThemeAsync(ElementTheme theme)
    {
        Theme = theme;

        await SetRequestedThemeAsync();
        await SaveThemeInSettingsAsync(Theme);
    }

    public async Task SetRequestedThemeAsync()
    {
        if (App.MainWindow.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = Theme;

            TitleBarHelper.UpdateTitleBar(Theme);
        }

        // Re-apply backdrop in case it's set to None — its solid background
        // depends on the current theme and must be refreshed.
        await App.GetService<IBackdropSelectorService>().SetRequestedBackdropAsync();
    }

    private async Task<ElementTheme> LoadThemeFromSettingsAsync()
    {
        var themeName = await _localSettingsService.ReadSettingAsync<string>(SettingsKey);

        if (Enum.TryParse(themeName, out ElementTheme cacheTheme))
        {
            return cacheTheme;
        }

        return ElementTheme.Default;
    }

    private async Task SaveThemeInSettingsAsync(ElementTheme theme)
    {
        await _localSettingsService.SaveSettingAsync(SettingsKey, theme.ToString());
    }
}
