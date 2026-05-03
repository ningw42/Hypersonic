using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class SettingsViewModelGeneralTests
{
    [TestMethod]
    public void TogglingMinimizeToTray_PersistsAndRefreshesTrayVisibility()
    {
        var appState = new FakeAppStateService();
        var tray = new FakeTrayIconService();
        var vm = CreateViewModel(appState: appState, tray: tray);

        // Hydration in the constructor should not count as a change.
        Assert.AreEqual(0, appState.GeneralSaveCount);
        Assert.AreEqual(0, tray.RefreshVisibilityCount);

        vm.MinimizeToTray = true;

        Assert.IsTrue(appState.State.General.MinimizeToTray);
        Assert.AreEqual(1, appState.GeneralSaveCount);
        Assert.AreEqual(1, tray.RefreshVisibilityCount);
    }

    [TestMethod]
    public void TogglingCloseToTray_PersistsAndRefreshesTrayVisibility()
    {
        var appState = new FakeAppStateService();
        var tray = new FakeTrayIconService();
        var vm = CreateViewModel(appState: appState, tray: tray);

        Assert.AreEqual(0, appState.GeneralSaveCount);
        Assert.AreEqual(0, tray.RefreshVisibilityCount);

        vm.CloseToTray = true;

        Assert.IsTrue(appState.State.General.CloseToTray);
        Assert.AreEqual(1, appState.GeneralSaveCount);
        Assert.AreEqual(1, tray.RefreshVisibilityCount);
    }

    // --- Test infrastructure ---

    private static SettingsViewModel CreateViewModel(
        FakeAppStateService? appState = null,
        FakeTrayIconService? tray = null)
    {
        return new SettingsViewModel(
            new GeneralFakeThemeSelectorService(),
            new GeneralFakeBackdropSelectorService(),
            new GeneralFakeScrobblingService(),
            new GeneralNoopLocalSettingsService(),
            appState ?? new FakeAppStateService(),
            tray ?? new FakeTrayIconService());
    }

    private sealed class GeneralFakeThemeSelectorService : IThemeSelectorService
    {
        public ElementTheme Theme => ElementTheme.Default;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SetThemeAsync(ElementTheme theme) => Task.CompletedTask;
        public Task SetRequestedThemeAsync() => Task.CompletedTask;
    }

    private sealed class GeneralFakeBackdropSelectorService : IBackdropSelectorService
    {
        public BackdropType Backdrop => BackdropType.Mica;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SetBackdropAsync(BackdropType backdrop) => Task.CompletedTask;
        public Task SetRequestedBackdropAsync() => Task.CompletedTask;
    }

    private sealed class GeneralFakeScrobblingService : IScrobblingService
    {
        public bool IsEnabled => false;
        public bool NowPlayingEnabled => false;
        public int PlaytimeThreshold => 30;
        public int PercentageThreshold => 50;
        public ScrobbleConditionMode ConditionMode => ScrobbleConditionMode.Or;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SetEnabledAsync(bool enabled) => Task.CompletedTask;
        public Task SetNowPlayingEnabledAsync(bool enabled) => Task.CompletedTask;
        public Task SetPlaytimeThresholdAsync(int seconds) => Task.CompletedTask;
        public Task SetPercentageThresholdAsync(int percentage) => Task.CompletedTask;
        public Task SetConditionModeAsync(ScrobbleConditionMode mode) => Task.CompletedTask;
    }

    private sealed class GeneralNoopLocalSettingsService : ILocalSettingsService
    {
        public Task<T?> ReadSettingAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SaveSettingAsync<T>(string key, T value) => Task.CompletedTask;
        public Task RemoveSettingAsync(string key) => Task.CompletedTask;
    }
}
