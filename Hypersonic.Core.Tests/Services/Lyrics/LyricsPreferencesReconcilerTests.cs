using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class LyricsPreferencesReconcilerTests
{
    private static readonly string[] Registered = ["subsonic", "lrclib", "amll-ttml-db", "qq", "netease", "kugou"];

    private static LyricsPreferencesState Reconcile(LyricsPreferencesState? persisted)
        => LyricsPreferencesReconciler.Reconcile(persisted, Registered, defaultAutoLoadMs: 700, defaultManualRefreshMs: 5000);

    [TestMethod]
    public void FirstRun_AllEnabled_DefaultOrder_DefaultBudgets()
    {
        var state = Reconcile(null);

        CollectionAssert.AreEqual(Registered, state.Providers.Select(p => p.Id).ToArray());
        Assert.IsTrue(state.Providers.All(p => p.Enabled));
        Assert.AreEqual(700, state.AutoLoadBudgetMs);
        Assert.AreEqual(5000, state.ManualRefreshBudgetMs);
    }

    [TestMethod]
    public void Unknown_Ids_Dropped()
    {
        var persisted = new LyricsPreferencesState
        {
            Providers = [new() { Id = "ghost", Enabled = true }, new() { Id = "lrclib", Enabled = true }],
            AutoLoadBudgetMs = 700,
            ManualRefreshBudgetMs = 5000,
        };

        var state = Reconcile(persisted);

        Assert.IsFalse(state.Providers.Any(p => p.Id == "ghost"));
    }

    [TestMethod]
    public void NewRegistered_Ids_Appended_Enabled_InCanonicalOrder()
    {
        // Persisted knows only the two original providers; the four newer ones must be appended, enabled.
        var persisted = new LyricsPreferencesState
        {
            Providers = [new() { Id = "subsonic", Enabled = true }, new() { Id = "lrclib", Enabled = false }],
            AutoLoadBudgetMs = 700,
            ManualRefreshBudgetMs = 5000,
        };

        var state = Reconcile(persisted);

        // subsonic (enabled) first; the appended new ids are enabled; lrclib (disabled) sinks to the end.
        Assert.AreEqual("subsonic", state.Providers[0].Id);
        Assert.IsTrue(state.Providers.Single(p => p.Id == "amll-ttml-db").Enabled);
        Assert.IsTrue(state.Providers.Single(p => p.Id == "qq").Enabled);
        Assert.AreEqual("lrclib", state.Providers[^1].Id);
        Assert.IsFalse(state.Providers[^1].Enabled);
    }

    [TestMethod]
    public void EnabledFirst_Ordering_IsEnforced()
    {
        var persisted = new LyricsPreferencesState
        {
            Providers =
            [
                new() { Id = "subsonic", Enabled = false },
                new() { Id = "lrclib", Enabled = true },
                new() { Id = "amll-ttml-db", Enabled = false },
                new() { Id = "qq", Enabled = true },
                new() { Id = "netease", Enabled = true },
                new() { Id = "kugou", Enabled = true },
            ],
            AutoLoadBudgetMs = 700,
            ManualRefreshBudgetMs = 5000,
        };

        var state = Reconcile(persisted);
        var ids = state.Providers.Select(p => p.Id).ToArray();

        // Enabled keep their relative order, then disabled keep theirs.
        CollectionAssert.AreEqual(new[] { "lrclib", "qq", "netease", "kugou", "subsonic", "amll-ttml-db" }, ids);
    }

    [TestMethod]
    public void AllDisabledPersisted_EnablesFirstEntry()
    {
        var persisted = new LyricsPreferencesState
        {
            Providers = Registered.Select(id => new LyricsProviderPreference { Id = id, Enabled = false }).ToList(),
            AutoLoadBudgetMs = 700,
            ManualRefreshBudgetMs = 5000,
        };

        var state = Reconcile(persisted);

        Assert.AreEqual(1, state.Providers.Count(p => p.Enabled));
        Assert.IsTrue(state.Providers[0].Enabled);
    }

    [TestMethod]
    public void Budget_OutOfRange_Clamped()
    {
        var persisted = new LyricsPreferencesState
        {
            Providers = [new() { Id = "subsonic", Enabled = true }],
            AutoLoadBudgetMs = 99999,
            ManualRefreshBudgetMs = 10,
        };

        var state = Reconcile(persisted);

        Assert.AreEqual(3000, state.AutoLoadBudgetMs);
        Assert.AreEqual(1000, state.ManualRefreshBudgetMs);
    }

    [TestMethod]
    public void Budget_MissingOrZero_FallsBackToDefault()
    {
        var persisted = new LyricsPreferencesState
        {
            Providers = [new() { Id = "subsonic", Enabled = true }],
            AutoLoadBudgetMs = 0,
            ManualRefreshBudgetMs = 0,
        };

        var state = Reconcile(persisted);

        Assert.AreEqual(700, state.AutoLoadBudgetMs);
        Assert.AreEqual(5000, state.ManualRefreshBudgetMs);
    }

    [TestMethod]
    public void RoundTrip_PreservesEnabledOrderAndFlags()
    {
        var persisted = new LyricsPreferencesState
        {
            Providers =
            [
                new() { Id = "lrclib", Enabled = true },
                new() { Id = "subsonic", Enabled = true },
                new() { Id = "amll-ttml-db", Enabled = true },
                new() { Id = "qq", Enabled = true },
                new() { Id = "netease", Enabled = true },
                new() { Id = "kugou", Enabled = false },
            ],
            AutoLoadBudgetMs = 1200,
            ManualRefreshBudgetMs = 8000,
        };

        var state = Reconcile(persisted);

        CollectionAssert.AreEqual(
            new[] { "lrclib", "subsonic", "amll-ttml-db", "qq", "netease", "kugou" },
            state.Providers.Select(p => p.Id).ToArray());
        Assert.IsFalse(state.Providers[^1].Enabled);
        Assert.AreEqual(1200, state.AutoLoadBudgetMs);
        Assert.AreEqual(8000, state.ManualRefreshBudgetMs);
    }
}
