using Hypersonic.Contracts.Services;
using Hypersonic.Tests.Fakes;
using Hypersonic.ViewModels;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class LyricsSettingsViewModelTests
{
    private static FakeLyricsPreferences Prefs()
    {
        var ids = new[] { "subsonic", "lrclib", "amll-ttml-db", "qq", "netease", "kugou" };
        return new FakeLyricsPreferences
        {
            DefaultProviderOrder = ids,
            Providers = ids.Select(id => new LyricsProviderView(id, id.ToUpperInvariant(), true)).ToList(),
            AutoLoadBudget = TimeSpan.FromMilliseconds(700),
            ManualRefreshBudget = TimeSpan.FromMilliseconds(5000),
            DefaultAutoLoadBudget = TimeSpan.FromMilliseconds(700),
            DefaultManualRefreshBudget = TimeSpan.FromMilliseconds(5000),
        };
    }

    private static async Task<LyricsSettingsViewModel> BuildAsync(FakeLyricsPreferences prefs)
    {
        var vm = new LyricsSettingsViewModel(prefs);
        await vm.InitializeAsync();
        return vm;
    }

    [TestMethod]
    public async Task Initialize_SplitsEnabledAndDisabled()
    {
        var prefs = Prefs();
        prefs.Providers = new List<LyricsProviderView>
        {
            new("subsonic", "Subsonic", true),
            new("lrclib", "LrcLib", true),
            new("qq", "QQ Music", false),
        };

        var vm = await BuildAsync(prefs);

        CollectionAssert.AreEqual(new[] { "subsonic", "lrclib" }, vm.EnabledProviders.Select(p => p.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "qq" }, vm.DisabledProviders.Select(p => p.Id).ToArray());
        Assert.IsTrue(vm.ShowDisabledDivider);
        Assert.IsTrue(vm.IsInitialized);
    }

    [TestMethod]
    public async Task DisableProvider_SinksToBottom_AndCallsPreferences()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);

        var subsonic = vm.EnabledProviders.First(p => p.Id == "subsonic");
        subsonic.IsEnabled = false;

        Assert.IsFalse(vm.EnabledProviders.Any(p => p.Id == "subsonic"));
        Assert.AreEqual("subsonic", vm.DisabledProviders[^1].Id);
        CollectionAssert.Contains(prefs.SetEnabledCalls, ("subsonic", false));
    }

    [TestMethod]
    public async Task ReenableProvider_AppendsToEndOfEnabled()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);

        var subsonic = vm.EnabledProviders.First(p => p.Id == "subsonic");
        subsonic.IsEnabled = false;
        subsonic.IsEnabled = true;

        Assert.AreEqual("subsonic", vm.EnabledProviders[^1].Id);
        CollectionAssert.Contains(prefs.SetEnabledCalls, ("subsonic", true));
    }

    [TestMethod]
    public async Task LastEnabledProvider_CannotToggle()
    {
        var prefs = Prefs();
        prefs.Providers = new List<LyricsProviderView> { new("subsonic", "Subsonic", true) };

        var vm = await BuildAsync(prefs);

        Assert.AreEqual(1, vm.EnabledProviders.Count);
        Assert.IsFalse(vm.EnabledProviders[0].CanToggle, "the sole enabled provider's checkbox is locked");
    }

    [TestMethod]
    public async Task Reorder_EnabledCollection_CallsPreferences()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);

        vm.EnabledProviders.Move(0, 1); // swap the first two

        Assert.AreEqual(1, prefs.ReorderCalls.Count);
        Assert.AreEqual(vm.EnabledProviders[0].Id, prefs.ReorderCalls[0][0]);
    }

    [TestMethod]
    public async Task AutoLoadSlider_Change_Persists_AndTracksReset()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);

        Assert.IsFalse(vm.CanResetAutoLoad);
        vm.AutoLoadSeconds = 1.5;

        Assert.AreEqual(TimeSpan.FromSeconds(1.5), prefs.AutoLoadBudget);
        Assert.IsTrue(vm.CanResetAutoLoad);
        Assert.AreEqual("1.5 s", vm.AutoLoadText);
    }

    [TestMethod]
    public async Task ResetAutoLoad_RestoresDefault()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);
        vm.AutoLoadSeconds = 2.0;

        vm.ResetAutoLoadCommand.Execute(null);

        Assert.AreEqual(0.7, vm.AutoLoadSeconds, 0.0001);
        Assert.IsFalse(vm.CanResetAutoLoad);
    }

    [TestMethod]
    public async Task ResetProviders_CallsPreferences_AndRebuilds()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);
        vm.EnabledProviders.First(p => p.Id == "qq").IsEnabled = false;

        vm.ResetProvidersCommand.Execute(null);

        Assert.AreEqual(1, prefs.ResetProvidersCount);
        Assert.AreEqual(6, vm.EnabledProviders.Count);
        Assert.AreEqual(0, vm.DisabledProviders.Count);
        Assert.IsFalse(vm.CanResetProviders);
    }
}
