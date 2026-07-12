using Hypersonic.Contracts.Services;
using Hypersonic.Tests.Fakes;
using Hypersonic.ViewModels;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class LyricsSettingsViewModelTests
{
    private static FakeLyricsPreferences Prefs()
    {
        var ids = new[] { "qq", "subsonic", "lrclib", "amll-ttml-db", "netease", "kugou" };
        return new FakeLyricsPreferences
        {
            DefaultProviderOrder = ids,
            DefaultEnabledProviderIds = new[] { "qq", "subsonic", "lrclib" },
            Providers = new List<LyricsProviderView>
            {
                new("qq", "QQ Music", true),
                new("subsonic", "Subsonic", true),
                new("lrclib", "LrcLib", true),
                new("amll-ttml-db", "AMLL", false),
                new("netease", "NetEase", false),
                new("kugou", "Kugou", false),
            },
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
    public async Task Initialize_ProjectsChips_AndSuggestions()
    {
        var vm = await BuildAsync(Prefs());

        CollectionAssert.AreEqual(new[] { "QQ Music", "Subsonic", "LrcLib" }, vm.EnabledProviders.Select(p => p.DisplayName).ToArray());
        CollectionAssert.AreEquivalent(new[] { "AMLL", "NetEase", "Kugou" }, vm.ProviderSuggestions.Select(p => p.DisplayName).ToArray());
        Assert.IsTrue(vm.IsInitialized);
        Assert.IsFalse(vm.CanResetProviders);
    }

    [TestMethod]
    public async Task ResolveCandidate_ExactMatchOnly()
    {
        var vm = await BuildAsync(Prefs());
        Assert.AreEqual("amll-ttml-db", vm.ResolveCandidate("AMLL")?.Id);
        Assert.IsNull(vm.ResolveCandidate("amll"));       // wrong case
        Assert.IsNull(vm.ResolveCandidate("QQ Music"));   // already enabled → not a candidate
    }

    [TestMethod]
    public async Task FilterSuggestions_ByQuery_CaseInsensitive()
    {
        var vm = await BuildAsync(Prefs());
        vm.FilterSuggestions("net");
        CollectionAssert.AreEqual(new[] { "NetEase" }, vm.ProviderSuggestions.Select(p => p.DisplayName).ToArray());
    }

    [TestMethod]
    public async Task EnableProvider_NormalizesToEnd_Persists_DropsSuggestion()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);
        var amll = vm.ProviderSuggestions.First(p => p.Id == "amll-ttml-db");
        vm.EnabledProviders.Insert(1, amll); // simulate TTB committing the chip mid-strip

        vm.EnableProvider(amll);

        Assert.AreEqual("amll-ttml-db", vm.EnabledProviders[^1].Id); // normalized to the end
        CollectionAssert.Contains(prefs.SetEnabledCalls, ("amll-ttml-db", true));
        Assert.IsFalse(vm.ProviderSuggestions.Any(p => p.Id == "amll-ttml-db"));
    }

    [TestMethod]
    public async Task DisableProvider_Persists_RestoresSuggestion()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);
        var subsonic = vm.EnabledProviders.First(p => p.Id == "subsonic");
        vm.EnabledProviders.Remove(subsonic); // simulate TTB removing the chip

        vm.DisableProvider(subsonic);

        CollectionAssert.Contains(prefs.SetEnabledCalls, ("subsonic", false));
        Assert.IsTrue(vm.ProviderSuggestions.Any(p => p.Id == "subsonic"));
    }

    [TestMethod]
    public async Task CanRemove_FalseWhenOneChip()
    {
        var prefs = Prefs();
        prefs.Providers = new List<LyricsProviderView> { new("qq", "QQ Music", true) };
        prefs.DefaultEnabledProviderIds = new[] { "qq" };

        var vm = await BuildAsync(prefs);

        Assert.AreEqual(1, vm.EnabledProviders.Count);
        Assert.IsFalse(vm.CanRemove);
    }

    [TestMethod]
    public async Task CommitReorder_MapsToIds_AndPersists()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);
        var reordered = new[] { vm.EnabledProviders[1], vm.EnabledProviders[0], vm.EnabledProviders[2] };

        vm.CommitReorder(reordered);

        Assert.AreEqual(1, prefs.ReorderCalls.Count);
        CollectionAssert.AreEqual(new[] { "subsonic", "qq", "lrclib" }, prefs.ReorderCalls[0].ToArray());
    }

    [TestMethod]
    public async Task Reset_RebuildsDefaults_RefreshesSuggestions_ClearsCanReset()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);
        var amll = vm.ProviderSuggestions.First(p => p.Id == "amll-ttml-db");
        vm.EnabledProviders.Add(amll);
        vm.EnableProvider(amll);
        Assert.IsTrue(vm.CanResetProviders);

        vm.ResetProvidersCommand.Execute(null);

        Assert.AreEqual(1, prefs.ResetProvidersCount);
        CollectionAssert.AreEqual(new[] { "QQ Music", "Subsonic", "LrcLib" }, vm.EnabledProviders.Select(p => p.DisplayName).ToArray());
        CollectionAssert.AreEquivalent(new[] { "AMLL", "NetEase", "Kugou" }, vm.ProviderSuggestions.Select(p => p.DisplayName).ToArray());
        Assert.IsFalse(vm.CanResetProviders);
    }

    [TestMethod]
    public async Task CustomOnlyProviders_ExposeConsistentResetState()
    {
        var prefs = new FakeLyricsPreferences
        {
            DefaultProviderOrder = new[] { "custom-first", "custom-second" },
            Providers =
            [
                new("custom-first", "Custom First", true),
                new("custom-second", "Custom Second", false),
            ],
        };
        var vm = await BuildAsync(prefs);

        Assert.IsFalse(vm.CanResetProviders);

        var second = vm.ProviderSuggestions.Single(p => p.Id == "custom-second");
        vm.EnabledProviders.Add(second);
        vm.EnableProvider(second);
        Assert.IsTrue(vm.CanResetProviders);

        vm.ResetProvidersCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "custom-first" }, vm.EnabledProviders.Select(p => p.Id).ToArray());
        Assert.IsFalse(vm.CanResetProviders);
    }

    [TestMethod]
    public async Task AutoLoadSlider_Change_Persists_AndTracksReset()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);

        Assert.IsFalse(vm.CanResetAutoLoad);
        vm.AutoLoadSeconds = 1.5;

        Assert.AreEqual(TimeSpan.FromSeconds(1.5), prefs.AutoLoadBudget); // persisted to the service
        Assert.IsTrue(vm.CanResetAutoLoad);
        Assert.AreEqual("1.5 s", vm.AutoLoadText);
    }

    [TestMethod]
    public async Task ResetAutoLoad_RestoresDefault()
    {
        var vm = await BuildAsync(Prefs());
        vm.AutoLoadSeconds = 2.0;

        vm.ResetAutoLoadCommand.Execute(null);

        Assert.AreEqual(0.7, vm.AutoLoadSeconds, 0.0001);
        Assert.IsFalse(vm.CanResetAutoLoad);
    }
}
