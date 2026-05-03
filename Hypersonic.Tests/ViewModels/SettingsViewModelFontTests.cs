using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class SettingsViewModelFontTests
{
    // --- Initial state ---

    [TestMethod]
    public async Task Initial_State_EmptyAndDefaults()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        Assert.AreEqual(0, vm.UiFontFamilies.Count);
        Assert.AreEqual(0, vm.LyricsFontFamilies.Count);
        Assert.AreEqual(1.0, vm.UiScale);
        Assert.AreEqual(1, vm.UiScaleIndex);

        Assert.IsFalse(vm.CanResetUiFonts);
        Assert.IsFalse(vm.CanResetLyricsFonts);
        Assert.IsFalse(vm.CanResetUiScale);
        Assert.IsFalse(vm.IsFontRestartRequired);
        Assert.IsTrue(vm.IsFontSettingsInitialized);
    }

    [TestMethod]
    public async Task FontSettings_AreUnavailableUntilInitializationCompletes()
    {
        var settings = new FakeLocalSettingsService
        {
            ReadGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };

        var vm = CreateViewModel(settings);

        Assert.IsFalse(vm.IsFontSettingsInitialized);

        settings.ReadGate.SetResult(true);
        await vm.InitializeFontSettingsAsync();

        Assert.IsTrue(vm.IsFontSettingsInitialized);
    }

    // --- Persistence: write path ---

    [TestMethod]
    public async Task UiFontFamilies_Add_Persists()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiFontFamilies.Add("Cascadia Code");
        vm.UiFontFamilies.Add("Microsoft YaHei UI");

        var stored = settings.GetAs<List<string>>(SettingsViewModel.UiFontFamiliesKey);
        Assert.IsNotNull(stored);
        CollectionAssert.AreEqual(new[] { "Cascadia Code", "Microsoft YaHei UI" }, stored!);
    }

    [TestMethod]
    public async Task LyricsFontFamilies_Add_Persists()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.LyricsFontFamilies.Add("Georgia");

        var stored = settings.GetAs<List<string>>(SettingsViewModel.LyricsFontFamiliesKey);
        Assert.IsNotNull(stored);
        CollectionAssert.AreEqual(new[] { "Georgia" }, stored!);
    }

    [TestMethod]
    public async Task UiFontFamilies_Reorder_Persists()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiFontFamilies.Add("Cascadia Code");
        vm.UiFontFamilies.Add("Calibri");
        vm.UiFontFamilies.Move(0, 1);

        var stored = settings.GetAs<List<string>>(SettingsViewModel.UiFontFamiliesKey);
        Assert.IsNotNull(stored);
        CollectionAssert.AreEqual(new[] { "Calibri", "Cascadia Code" }, stored!);
    }

    [TestMethod]
    public async Task UiFontFamilies_Remove_Persists()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiFontFamilies.Add("Cascadia Code");
        vm.UiFontFamilies.Add("Calibri");
        vm.UiFontFamilies.RemoveAt(0);

        var stored = settings.GetAs<List<string>>(SettingsViewModel.UiFontFamiliesKey);
        Assert.IsNotNull(stored);
        CollectionAssert.AreEqual(new[] { "Calibri" }, stored!);
    }

    [TestMethod]
    public async Task FontFamilies_BlankEntries_AreNotPersisted()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiFontFamilies.Add("Cascadia Code");
        vm.UiFontFamilies.Add("");
        vm.UiFontFamilies.Add("  ");
        vm.UiFontFamilies.Add("  Calibri  ");
        vm.LyricsFontFamilies.Add("  ");
        vm.LyricsFontFamilies.Add("  Georgia  ");

        var storedUi = settings.GetAs<List<string>>(SettingsViewModel.UiFontFamiliesKey);
        var storedLyrics = settings.GetAs<List<string>>(SettingsViewModel.LyricsFontFamiliesKey);
        Assert.IsNotNull(storedUi);
        Assert.IsNotNull(storedLyrics);
        CollectionAssert.AreEqual(new[] { "Cascadia Code", "Calibri" }, storedUi!);
        CollectionAssert.AreEqual(new[] { "Georgia" }, storedLyrics!);
    }

    [TestMethod]
    public async Task UiScale_Change_Persists()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiScale = 1.25;

        Assert.AreEqual(1.25, settings.GetAs<double>(SettingsViewModel.UiScaleKey));
    }

    [TestMethod]
    public async Task FontSettingSaves_AreSerializedInOrder()
    {
        var settings = new FakeLocalSettingsService();
        var firstSaveEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSaveEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiFontSaveCount = 0;

        settings.OnBeforeSaveAsync = async (key, value) =>
        {
            if (key != SettingsViewModel.UiFontFamiliesKey)
            {
                return;
            }

            uiFontSaveCount++;
            if (uiFontSaveCount == 1)
            {
                firstSaveEntered.SetResult(true);
                await releaseFirstSave.Task;
            }
            else if (uiFontSaveCount == 2)
            {
                secondSaveEntered.SetResult(true);
            }
        };

        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiFontFamilies.Add("Arial");
        Assert.IsTrue(await CompletesWithinAsync(firstSaveEntered.Task, 1000));

        vm.UiFontFamilies.Add("Calibri");
        Assert.IsFalse(await CompletesWithinAsync(secondSaveEntered.Task, 50));

        releaseFirstSave.SetResult(true);
        await vm.WaitForFontSettingsPersistenceAsync();

        Assert.IsTrue(await CompletesWithinAsync(secondSaveEntered.Task, 1000));

        var stored = settings.GetAs<List<string>>(SettingsViewModel.UiFontFamiliesKey);
        Assert.IsNotNull(stored);
        CollectionAssert.AreEqual(new[] { "Arial", "Calibri" }, stored!);
    }

    [TestMethod]
    public async Task UiScaleIndex_Change_RoundTripsThroughUiScale()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiScaleIndex = 3; // 1.25

        Assert.AreEqual(1.25, vm.UiScale);
        Assert.AreEqual(1.25, settings.GetAs<double>(SettingsViewModel.UiScaleKey));
        Assert.AreEqual(3, vm.UiScaleIndex);
    }

    [TestMethod]
    public async Task UiScale_ChangeMarksRestartRequiredAndRevertClears()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiScale = 1.25;
        Assert.IsTrue(vm.IsFontRestartRequired);

        vm.UiScale = 1.0;
        Assert.IsFalse(vm.IsFontRestartRequired);
    }

    [TestMethod]
    public async Task UiScaleIndex_OutOfRange_IsNoOp()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        var initialScale = vm.UiScale;
        vm.UiScaleIndex = -1;
        vm.UiScaleIndex = 99;

        Assert.AreEqual(initialScale, vm.UiScale);
    }

    // --- Persistence: read path ---

    [TestMethod]
    public async Task PersistedFonts_RestoredOnInit()
    {
        var settings = new FakeLocalSettingsService();
        settings.Store[SettingsViewModel.UiFontFamiliesKey] = new List<string> { "Cascadia Code", "Microsoft YaHei UI" };
        settings.Store[SettingsViewModel.LyricsFontFamiliesKey] = new List<string> { "Georgia" };
        settings.Store[SettingsViewModel.UiScaleKey] = (double?)1.15;

        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        CollectionAssert.AreEqual(new[] { "Cascadia Code", "Microsoft YaHei UI" }, vm.UiFontFamilies.ToArray());
        CollectionAssert.AreEqual(new[] { "Georgia" }, vm.LyricsFontFamilies.ToArray());
        Assert.AreEqual(1.15, vm.UiScale);
        Assert.AreEqual(2, vm.UiScaleIndex);
        Assert.IsFalse(vm.IsFontRestartRequired);
    }

    [TestMethod]
    public async Task PersistedFonts_BlankEntries_AreSkipped()
    {
        var settings = new FakeLocalSettingsService();
        settings.Store[SettingsViewModel.UiFontFamiliesKey] = new List<string> { "Cascadia Code", "", "  ", "Calibri" };

        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        CollectionAssert.AreEqual(new[] { "Cascadia Code", "Calibri" }, vm.UiFontFamilies.ToArray());
    }

    [TestMethod]
    public async Task Initialization_DoesNotPersistWhileSeeding()
    {
        var settings = new FakeLocalSettingsService();
        settings.Store[SettingsViewModel.UiFontFamiliesKey] = new List<string> { "Cascadia Code" };
        settings.Store[SettingsViewModel.UiScaleKey] = (double?)1.5;

        var vm = CreateViewModel(settings);
        // Construction triggers fire-and-forget init; await explicitly so the
        // fake's write-counter is settled.
        await vm.InitializeFontSettingsAsync();

        // Seeding the collection from persisted data should NOT have triggered
        // a write back through the persistence path.
        Assert.AreEqual(0, settings.WriteCount);
    }

    // --- Reset commands ---

    [TestMethod]
    public async Task ResetUiFontsCommand_ClearsAndPersists()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiFontFamilies.Add("Cascadia Code");
        vm.UiFontFamilies.Add("Calibri");
        Assert.IsTrue(vm.CanResetUiFonts);

        vm.ResetUiFontsCommand.Execute(null);

        Assert.AreEqual(0, vm.UiFontFamilies.Count);
        Assert.IsFalse(vm.CanResetUiFonts);

        var stored = settings.GetAs<List<string>>(SettingsViewModel.UiFontFamiliesKey);
        Assert.IsNotNull(stored);
        Assert.AreEqual(0, stored!.Count);
    }

    [TestMethod]
    public async Task ResetLyricsFontsCommand_ClearsAndPersists()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.LyricsFontFamilies.Add("Georgia");
        Assert.IsTrue(vm.CanResetLyricsFonts);

        vm.ResetLyricsFontsCommand.Execute(null);

        Assert.AreEqual(0, vm.LyricsFontFamilies.Count);
        Assert.IsFalse(vm.CanResetLyricsFonts);
    }

    [TestMethod]
    public async Task ResetUiScaleCommand_ResetsToOneAndPersists()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiScale = 1.5;
        Assert.IsTrue(vm.CanResetUiScale);

        vm.ResetUiScaleCommand.Execute(null);

        Assert.AreEqual(1.0, vm.UiScale);
        Assert.IsFalse(vm.CanResetUiScale);
        Assert.AreEqual(1.0, settings.GetAs<double>(SettingsViewModel.UiScaleKey));
    }

    [TestMethod]
    public async Task CanResetFlags_TrackUnderlyingState()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        Assert.IsFalse(vm.CanResetUiFonts);
        vm.UiFontFamilies.Add("Calibri");
        Assert.IsTrue(vm.CanResetUiFonts);
        vm.UiFontFamilies.Clear();
        Assert.IsFalse(vm.CanResetUiFonts);

        Assert.IsFalse(vm.CanResetUiScale);
        vm.UiScale = 0.9;
        Assert.IsTrue(vm.CanResetUiScale);
        vm.UiScale = 1.0;
        Assert.IsFalse(vm.CanResetUiScale);
    }

    [TestMethod]
    public async Task FontCollectionChange_MarksRestartRequired()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiFontFamilies.Add("Calibri");

        Assert.IsTrue(vm.IsFontRestartRequired);
    }

    [TestMethod]
    public async Task FontCollectionReverted_ClearsRestartRequired()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiFontFamilies.Add("Calibri");
        Assert.IsTrue(vm.IsFontRestartRequired);

        vm.UiFontFamilies.Remove("Calibri");

        Assert.IsFalse(vm.IsFontRestartRequired);
    }

    [TestMethod]
    public async Task LyricsFontCollectionChange_MarksRestartRequired()
    {
        var settings = new FakeLocalSettingsService();
        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.LyricsFontFamilies.Add("Georgia");

        Assert.IsTrue(vm.IsFontRestartRequired);
    }

    [TestMethod]
    public async Task PersistedFontCollectionReverted_ClearsRestartRequired()
    {
        var settings = new FakeLocalSettingsService();
        settings.Store[SettingsViewModel.UiFontFamiliesKey] = new List<string> { "Cascadia Code" };

        var vm = CreateViewModel(settings);
        await vm.InitializeFontSettingsAsync();

        vm.UiFontFamilies.Remove("Cascadia Code");
        Assert.IsTrue(vm.IsFontRestartRequired);

        vm.UiFontFamilies.Add("Cascadia Code");

        Assert.IsFalse(vm.IsFontRestartRequired);
    }

    // --- Test infrastructure ---

    private static SettingsViewModel CreateViewModel(FakeLocalSettingsService settings)
    {
        return new SettingsViewModel(
            new FakeThemeSelectorService(),
            new FakeBackdropSelectorService(),
            new FakeScrobblingService(),
            settings,
            new FakeAppStateService(),
            new FakeTrayIconService());
    }

    private sealed class FakeLocalSettingsService : ILocalSettingsService
    {
        public Dictionary<string, object?> Store { get; } = [];
        public TaskCompletionSource<bool>? ReadGate { get; init; }
        public Func<string, object?, Task>? OnBeforeSaveAsync { get; set; }
        public int WriteCount { get; private set; }

        public T? GetAs<T>(string key)
        {
            if (!Store.TryGetValue(key, out var v) || v is null)
            {
                return default;
            }

            return (T)v;
        }

        public async Task<T?> ReadSettingAsync<T>(string key)
        {
            if (ReadGate != null)
            {
                await ReadGate.Task;
            }

            if (Store.TryGetValue(key, out var v) && v is not null)
            {
                return (T?)v;
            }

            return default;
        }

        public async Task SaveSettingAsync<T>(string key, T value)
        {
            if (OnBeforeSaveAsync != null)
            {
                await OnBeforeSaveAsync(key, value);
            }

            Store[key] = value;
            WriteCount++;
        }

        public Task RemoveSettingAsync(string key)
        {
            Store.Remove(key);
            return Task.CompletedTask;
        }
    }

    private static async Task<bool> CompletesWithinAsync(Task task, int milliseconds)
    {
        var completed = await Task.WhenAny(task, Task.Delay(milliseconds));
        return ReferenceEquals(completed, task);
    }

    private sealed class FakeThemeSelectorService : IThemeSelectorService
    {
        public ElementTheme Theme => ElementTheme.Default;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SetThemeAsync(ElementTheme theme) => Task.CompletedTask;
        public Task SetRequestedThemeAsync() => Task.CompletedTask;
    }

    private sealed class FakeBackdropSelectorService : IBackdropSelectorService
    {
        public BackdropType Backdrop => BackdropType.Mica;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SetBackdropAsync(BackdropType backdrop) => Task.CompletedTask;
        public Task SetRequestedBackdropAsync() => Task.CompletedTask;
    }

    private sealed class FakeScrobblingService : IScrobblingService
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
}
