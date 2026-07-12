using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services;
using Hypersonic.Tests.Fakes;

using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services;

[TestClass]
public class LyricsPreferencesTests
{
    private static readonly IOptions<LyricsOrchestrationOptions> Opts =
        Options.Create(new LyricsOrchestrationOptions()); // 700 / 5000

    private static StubLyricsProvider[] AllProviders() =>
    [
        new("subsonic", "Subsonic"),
        new("lrclib", "LrcLib"),
        new("amll-ttml-db", "AMLL"),
        new("qq", "QQ Music"),
        new("netease", "NetEase"),
        new("kugou", "Kugou"),
    ];

    private static LyricsPreferences Create(FakeLocalSettingsService settings, params StubLyricsProvider[] providers)
        => new(settings, providers, Opts, new NullLoggingService());

    [TestMethod]
    public void BeforeLoad_ReportsDefaultEnabledSet()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());

        CollectionAssert.AreEqual(new[] { "qq", "subsonic", "lrclib" }, prefs.EnabledProviderIds.ToArray());
        Assert.AreEqual(TimeSpan.FromMilliseconds(700), prefs.AutoLoadBudget);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5000), prefs.ManualRefreshBudget);
    }

    [TestMethod]
    public async Task Load_FirstRun_SeedsDefaultEnabledSet_InPriorityOrder()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());

        await prefs.EnsureLoadedAsync();

        CollectionAssert.AreEqual(new[] { "qq", "subsonic", "lrclib" }, prefs.EnabledProviderIds.ToArray());
        CollectionAssert.AreEqual(new[] { "qq", "subsonic", "lrclib" }, prefs.DefaultEnabledProviderIds.ToArray());
    }

    [TestMethod]
    public async Task Load_AppliesPersistedEnabledSetAndOrder()
    {
        var settings = new FakeLocalSettingsService();
        settings.Store["LyricsPreferences"] = new LyricsPreferencesState
        {
            Providers =
            [
                new() { Id = "lrclib", Enabled = true },
                new() { Id = "subsonic", Enabled = true },
                new() { Id = "qq", Enabled = false },
            ],
            AutoLoadBudgetMs = 1500,
            ManualRefreshBudgetMs = 9000,
        };
        var prefs = Create(settings, AllProviders());

        await prefs.EnsureLoadedAsync();

        Assert.AreEqual("lrclib", prefs.EnabledProviderIds[0]);
        Assert.AreEqual("subsonic", prefs.EnabledProviderIds[1]);
        Assert.IsFalse(prefs.EnabledProviderIds.Contains("qq"));
        Assert.AreEqual(TimeSpan.FromMilliseconds(1500), prefs.AutoLoadBudget);
        Assert.AreEqual(TimeSpan.FromMilliseconds(9000), prefs.ManualRefreshBudget);

        // qq is present but disabled and sits after the enabled run in the UI view.
        var view = prefs.GetProviders();
        Assert.IsFalse(view.Single(v => v.Id == "qq").Enabled);
        Assert.AreEqual("QQ Music", view.Single(v => v.Id == "qq").DisplayName);
    }

    [TestMethod]
    public async Task Load_IsIdempotent()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());

        var a = prefs.EnsureLoadedAsync();
        var b = prefs.EnsureLoadedAsync();

        Assert.AreSame(a, b);
        await a;
    }

    [TestMethod]
    public void Defaults_ExposeCanonicalOrderAndBudgets()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());

        CollectionAssert.AreEqual(
            new[] { "subsonic", "lrclib", "amll-ttml-db", "qq", "netease", "kugou" },
            prefs.DefaultProviderOrder.ToArray());
        Assert.AreEqual(TimeSpan.FromMilliseconds(700), prefs.DefaultAutoLoadBudget);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5000), prefs.DefaultManualRefreshBudget);
    }

    [TestMethod]
    public async Task SetEnabled_Disable_SinksToBottom_AndPersists()
    {
        var settings = new FakeLocalSettingsService();
        var prefs = Create(settings, AllProviders());
        await prefs.EnsureLoadedAsync();

        prefs.SetEnabled("subsonic", false);
        await prefs.WaitForPendingSavesAsync();

        Assert.IsFalse(prefs.EnabledProviderIds.Contains("subsonic"));
        Assert.AreEqual("subsonic", prefs.GetProviders()[^1].Id, "disabled sinks to the bottom");

        var saved = settings.GetAs<LyricsPreferencesState>("LyricsPreferences");
        Assert.IsNotNull(saved);
        Assert.IsFalse(saved!.Providers.Single(p => p.Id == "subsonic").Enabled);
    }

    [TestMethod]
    public async Task SetEnabled_Reenable_AppendsToEndOfEnabledRun()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());
        await prefs.EnsureLoadedAsync();

        prefs.SetEnabled("subsonic", false); // now disabled at bottom
        prefs.SetEnabled("subsonic", true);  // re-enabled → end of enabled run

        var enabled = prefs.EnabledProviderIds;
        Assert.AreEqual("subsonic", enabled[^1], "re-enabled provider joins the end of the enabled run");
    }

    [TestMethod]
    public async Task SetEnabled_CannotDisableLastEnabled()
    {
        var settings = new FakeLocalSettingsService();
        // Only one provider registered → it is the sole enabled one.
        var prefs = Create(settings, new StubLyricsProvider("subsonic", "Subsonic"));
        await prefs.EnsureLoadedAsync();

        prefs.SetEnabled("subsonic", false);

        CollectionAssert.AreEqual(new[] { "subsonic" }, prefs.EnabledProviderIds.ToArray(), "the last enabled provider cannot be disabled");
    }

    [TestMethod]
    public async Task Reorder_ReordersEnabledRun_AndPersists()
    {
        var settings = new FakeLocalSettingsService();
        var prefs = Create(settings, AllProviders());
        await prefs.EnsureLoadedAsync();

        prefs.Reorder(["lrclib", "subsonic", "amll-ttml-db", "qq", "netease", "kugou"]);
        await prefs.WaitForPendingSavesAsync();

        Assert.AreEqual("lrclib", prefs.EnabledProviderIds[0]);
        Assert.AreEqual("subsonic", prefs.EnabledProviderIds[1]);

        var saved = settings.GetAs<LyricsPreferencesState>("LyricsPreferences");
        Assert.AreEqual("lrclib", saved!.Providers[0].Id);
    }

    [TestMethod]
    public async Task SetAutoLoadBudget_ClampsToRange_AndPersists()
    {
        var settings = new FakeLocalSettingsService();
        var prefs = Create(settings, AllProviders());
        await prefs.EnsureLoadedAsync();

        prefs.SetAutoLoadBudget(TimeSpan.FromSeconds(99)); // above 3000 ms max
        await prefs.WaitForPendingSavesAsync();

        Assert.AreEqual(TimeSpan.FromMilliseconds(3000), prefs.AutoLoadBudget);
        Assert.AreEqual(3000, settings.GetAs<LyricsPreferencesState>("LyricsPreferences")!.AutoLoadBudgetMs);
    }

    [TestMethod]
    public async Task SetManualRefreshBudget_ClampsToRange()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());
        await prefs.EnsureLoadedAsync();

        prefs.SetManualRefreshBudget(TimeSpan.FromMilliseconds(10)); // below 1000 ms min

        Assert.AreEqual(TimeSpan.FromMilliseconds(1000), prefs.ManualRefreshBudget);
    }

    [TestMethod]
    public async Task ResetProviders_RestoresDefaultEnabledSet_KeepsBudgets()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());
        await prefs.EnsureLoadedAsync();
        prefs.SetEnabled("amll-ttml-db", true);            // dirty the enabled set
        prefs.SetAutoLoadBudget(TimeSpan.FromSeconds(2));  // dirty a budget

        prefs.ResetProviders();

        CollectionAssert.AreEqual(new[] { "qq", "subsonic", "lrclib" }, prefs.EnabledProviderIds.ToArray());
        Assert.AreEqual(TimeSpan.FromSeconds(2), prefs.AutoLoadBudget, "reset providers leaves budgets alone");
    }

    [TestMethod]
    public async Task ResetProviders_WithoutRegisteredDefaults_EnablesFirstProviderAndKeepsBudgets()
    {
        var prefs = Create(
            new FakeLocalSettingsService(),
            new StubLyricsProvider("custom-first", "Custom First"),
            new StubLyricsProvider("custom-second", "Custom Second"));
        await prefs.EnsureLoadedAsync();
        prefs.SetAutoLoadBudget(TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(new[] { "custom-first" }, prefs.DefaultEnabledProviderIds.ToArray());

        prefs.ResetProviders();

        CollectionAssert.AreEqual(new[] { "custom-first" }, prefs.EnabledProviderIds.ToArray());
        Assert.AreEqual(TimeSpan.FromSeconds(2), prefs.AutoLoadBudget, "reset providers leaves budgets alone");
    }
}
