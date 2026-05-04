using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class SettingsViewModelSegmentedTests
{
    // --- ElementThemeIndex ---

    [TestMethod]
    public void ElementThemeIndex_Get_ReflectsElementTheme()
    {
        var vm = CreateViewModel();

        vm.ElementTheme = ElementTheme.Light;
        Assert.AreEqual(0, vm.ElementThemeIndex);

        vm.ElementTheme = ElementTheme.Dark;
        Assert.AreEqual(1, vm.ElementThemeIndex);

        vm.ElementTheme = ElementTheme.Default;
        Assert.AreEqual(2, vm.ElementThemeIndex);
    }

    [TestMethod]
    public void ElementThemeIndex_Set_UpdatesElementThemeAndCallsService()
    {
        var theme = new FakeThemeSelectorService();
        var vm = CreateViewModel(theme: theme);

        vm.ElementThemeIndex = 0;
        Assert.AreEqual(ElementTheme.Light, vm.ElementTheme);

        vm.ElementThemeIndex = 1;
        Assert.AreEqual(ElementTheme.Dark, vm.ElementTheme);

        vm.ElementThemeIndex = 2;
        Assert.AreEqual(ElementTheme.Default, vm.ElementTheme);

        CollectionAssert.AreEqual(
            new[] { ElementTheme.Light, ElementTheme.Dark, ElementTheme.Default },
            theme.SetCalls);
    }

    [TestMethod]
    public void ElementThemeIndex_Set_OutOfRange_IsNoOp()
    {
        var theme = new FakeThemeSelectorService();
        var vm = CreateViewModel(theme: theme);
        vm.ElementTheme = ElementTheme.Dark;

        vm.ElementThemeIndex = -1;
        vm.ElementThemeIndex = 3;
        vm.ElementThemeIndex = 99;

        Assert.AreEqual(ElementTheme.Dark, vm.ElementTheme);
        Assert.AreEqual(0, theme.SetCalls.Count);
    }

    // --- BackdropTypeIndex ---

    [TestMethod]
    public void BackdropTypeIndex_Get_ReflectsBackdropType()
    {
        var vm = CreateViewModel();

        vm.BackdropType = BackdropType.Mica;
        Assert.AreEqual(0, vm.BackdropTypeIndex);

        vm.BackdropType = BackdropType.MicaAlt;
        Assert.AreEqual(1, vm.BackdropTypeIndex);

        vm.BackdropType = BackdropType.Acrylic;
        Assert.AreEqual(2, vm.BackdropTypeIndex);

        vm.BackdropType = BackdropType.None;
        Assert.AreEqual(3, vm.BackdropTypeIndex);
    }

    [TestMethod]
    public void BackdropTypeIndex_Set_UpdatesBackdropTypeAndCallsService()
    {
        var backdrop = new FakeBackdropSelectorService();
        var vm = CreateViewModel(backdrop: backdrop);

        vm.BackdropTypeIndex = 1;
        Assert.AreEqual(BackdropType.MicaAlt, vm.BackdropType);

        vm.BackdropTypeIndex = 3;
        Assert.AreEqual(BackdropType.None, vm.BackdropType);

        CollectionAssert.AreEqual(
            new[] { BackdropType.MicaAlt, BackdropType.None },
            backdrop.SetCalls);
    }

    [TestMethod]
    public void BackdropTypeIndex_Set_OutOfRange_IsNoOp()
    {
        var backdrop = new FakeBackdropSelectorService();
        var vm = CreateViewModel(backdrop: backdrop);
        vm.BackdropType = BackdropType.Acrylic;

        vm.BackdropTypeIndex = -1;
        vm.BackdropTypeIndex = 4;
        vm.BackdropTypeIndex = 99;

        Assert.AreEqual(BackdropType.Acrylic, vm.BackdropType);
        Assert.AreEqual(0, backdrop.SetCalls.Count);
    }

    // --- ScrobbleConditionModeIndex ---

    [TestMethod]
    public void ScrobbleConditionModeIndex_Get_ReflectsConditionMode()
    {
        var vm = CreateViewModel();

        vm.ScrobbleConditionMode = ScrobbleConditionMode.Or;
        Assert.AreEqual(0, vm.ScrobbleConditionModeIndex);

        vm.ScrobbleConditionMode = ScrobbleConditionMode.And;
        Assert.AreEqual(1, vm.ScrobbleConditionModeIndex);
    }

    [TestMethod]
    public void ScrobbleConditionModeIndex_Set_UpdatesConditionModeAndCallsService()
    {
        var scrobbling = new FakeScrobblingService();
        var vm = CreateViewModel(scrobbling: scrobbling);

        vm.ScrobbleConditionModeIndex = 1;
        Assert.AreEqual(ScrobbleConditionMode.And, vm.ScrobbleConditionMode);

        vm.ScrobbleConditionModeIndex = 0;
        Assert.AreEqual(ScrobbleConditionMode.Or, vm.ScrobbleConditionMode);

        CollectionAssert.AreEqual(
            new[] { ScrobbleConditionMode.And, ScrobbleConditionMode.Or },
            scrobbling.SetConditionModeCalls);
    }

    [TestMethod]
    public void ScrobbleConditionModeIndex_Set_OutOfRange_IsNoOp()
    {
        var scrobbling = new FakeScrobblingService();
        var vm = CreateViewModel(scrobbling: scrobbling);
        vm.ScrobbleConditionMode = ScrobbleConditionMode.And;

        vm.ScrobbleConditionModeIndex = -1;
        vm.ScrobbleConditionModeIndex = 2;
        vm.ScrobbleConditionModeIndex = 99;

        Assert.AreEqual(ScrobbleConditionMode.And, vm.ScrobbleConditionMode);
        Assert.AreEqual(0, scrobbling.SetConditionModeCalls.Count);
    }

    // --- Test infrastructure ---

    private static SettingsViewModel CreateViewModel(
        FakeThemeSelectorService? theme = null,
        FakeBackdropSelectorService? backdrop = null,
        FakeScrobblingService? scrobbling = null,
        FakeAppStateService? appState = null,
        FakeTrayIconService? tray = null)
    {
        return new SettingsViewModel(
            theme ?? new FakeThemeSelectorService(),
            backdrop ?? new FakeBackdropSelectorService(),
            scrobbling ?? new FakeScrobblingService(),
            new NoopLocalSettingsService(),
            appState ?? new FakeAppStateService(),
            tray ?? new FakeTrayIconService(),
            new FakeImageCacheService());
    }

    private sealed class FakeThemeSelectorService : IThemeSelectorService
    {
        public List<ElementTheme> SetCalls { get; } = [];
        public ElementTheme Theme => ElementTheme.Default;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SetThemeAsync(ElementTheme theme)
        {
            SetCalls.Add(theme);
            return Task.CompletedTask;
        }
        public Task SetRequestedThemeAsync() => Task.CompletedTask;
    }

    private sealed class FakeBackdropSelectorService : IBackdropSelectorService
    {
        public List<BackdropType> SetCalls { get; } = [];
        public BackdropType Backdrop => BackdropType.Mica;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SetBackdropAsync(BackdropType backdrop)
        {
            SetCalls.Add(backdrop);
            return Task.CompletedTask;
        }
        public Task SetRequestedBackdropAsync() => Task.CompletedTask;
    }

    private sealed class FakeScrobblingService : IScrobblingService
    {
        public List<ScrobbleConditionMode> SetConditionModeCalls { get; } = [];
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
        public Task SetConditionModeAsync(ScrobbleConditionMode mode)
        {
            SetConditionModeCalls.Add(mode);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopLocalSettingsService : ILocalSettingsService
    {
        public Task<T?> ReadSettingAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SaveSettingAsync<T>(string key, T value) => Task.CompletedTask;
        public Task RemoveSettingAsync(string key) => Task.CompletedTask;
    }
}
