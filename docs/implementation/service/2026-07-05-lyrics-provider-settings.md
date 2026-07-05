# Lyrics Provider Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Lyrics" section to Settings that lets the user enable/disable each lyrics provider, drag them into a priority order, and tune the two fan-out wait windows — all persisted and applied live.

**Architecture:** A pure Core reconcile/clamp helper + persisted DTO owns the normalization rules. A new WinUI singleton `LyricsPreferences` owns the enabled set, order, and both budgets behind an atomically-swapped snapshot, persists itself via `ILocalSettingsService`, and reconciles against the registered providers on load. `LyricsService` consults it per-fetch to order+filter the fan-out and read the auto budget; `NowPlayingViewModel` reads the manual budget from it. A dedicated `LyricsSettingsViewModel` (resolved by `SettingsPage` alongside the existing `SettingsViewModel`, so the already-large `SettingsViewModel` is untouched) drives a two-collection provider list with sink-to-bottom disabled behavior and three inline per-control resets.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.2.0), CommunityToolkit.Mvvm, CommunityToolkit.WinUI.Controls (`SettingsExpander`/`SettingsCard`/`Segmented`), `System.Text.Json`, MSTest, `FakeTimeProvider`.

## Global Constraints

- **Code style:** 4-space indent, Allman braces, file-scoped namespaces, `var`, CRLF, interfaces `I`-prefixed. (`.editorconfig`)
- **Provider ids (verbatim):** `"subsonic"`, `"lrclib"`, `"amll-ttml-db"`, `"qq"`, `"netease"`, `"kugou"`. Reference the existing `*.ProviderId` constants, never string literals.
- **Canonical default order:** `subsonic → lrclib → amll-ttml-db → qq → netease → kugou` (matches current DI registration order but pinned explicitly).
- **Budget ranges:** auto-load `300–3000 ms` (default `700`); manual-refresh `1000–15000 ms` (default `5000`). Default *values* come from `LyricsOrchestrationOptions`; the range bounds are Core constants.
- **Persistence key:** exactly `"LyricsPreferences"` via `ILocalSettingsService` (packaged + unpackaged). One composite object, one atomic write.
- **Invariant:** at least one provider is always enabled — enforced in reconcile, in `SetEnabled`, and in the UI (the last enabled provider's checkbox locks).
- **Ordering invariant:** the persisted/snapshot provider list is always enabled-first (priority order) then disabled.
- **Live apply:** no restart; the next fetch reads the current snapshot.
- **Commits:** Conventional Commits with scope, e.g. `feat(lyrics): …`.
- **Test commands:** Core → `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`. WinUI → `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`. Single test → append `--filter "FullyQualifiedName~MethodName"`.
- **Design source:** [`docs/design/service/2026-07-05-lyrics-provider-settings.md`](../../design/service/2026-07-05-lyrics-provider-settings.md).

---

### Task 1: Core — persisted DTO + pure reconcile/clamp

The correctness crux. Pure, no WinUI/IO — the normalization rules are fully unit-tested here.

**Files:**
- Create: `Hypersonic.Core/Models/Lyrics/LyricsProviderPreference.cs`
- Create: `Hypersonic.Core/Models/Lyrics/LyricsPreferencesState.cs`
- Create: `Hypersonic.Core/Services/Lyrics/LyricsPreferencesReconciler.cs` (also holds the range constants)
- Test: `Hypersonic.Core.Tests/Services/Lyrics/LyricsPreferencesReconcilerTests.cs`

**Interfaces:**
- Produces:
  - `LyricsProviderPreference { string Id; bool Enabled }` (mutable, `get;set;`)
  - `LyricsPreferencesState { List<LyricsProviderPreference> Providers; int AutoLoadBudgetMs; int ManualRefreshBudgetMs }`
  - `static class LyricsPreferencesReconciler` with `const int AutoLoadMinMs=300, AutoLoadMaxMs=3000, ManualRefreshMinMs=1000, ManualRefreshMaxMs=15000` and `static LyricsPreferencesState Reconcile(LyricsPreferencesState? persisted, IReadOnlyList<string> registeredIdsInDefaultOrder, int defaultAutoLoadMs, int defaultManualRefreshMs)`

- [ ] **Step 1: Write the DTOs**

Create `Hypersonic.Core/Models/Lyrics/LyricsProviderPreference.cs`:

```csharp
namespace Hypersonic.Core.Models.Lyrics;

/// <summary>One persisted provider row: its id and whether it participates in the fan-out.</summary>
public sealed class LyricsProviderPreference
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
```

Create `Hypersonic.Core/Models/Lyrics/LyricsPreferencesState.cs`:

```csharp
namespace Hypersonic.Core.Models.Lyrics;

/// <summary>The persisted lyrics-preferences document (one ILocalSettingsService key). Providers are
/// stored in priority order with all enabled entries first, then all disabled.</summary>
public sealed class LyricsPreferencesState
{
    public List<LyricsProviderPreference> Providers { get; set; } = [];
    public int AutoLoadBudgetMs { get; set; }
    public int ManualRefreshBudgetMs { get; set; }
}
```

- [ ] **Step 2: Write the failing reconcile tests**

Create `Hypersonic.Core.Tests/Services/Lyrics/LyricsPreferencesReconcilerTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsPreferencesReconcilerTests"`
Expected: FAIL to compile — `LyricsPreferencesReconciler` does not exist.

- [ ] **Step 4: Implement the reconciler**

Create `Hypersonic.Core/Services/Lyrics/LyricsPreferencesReconciler.cs`:

```csharp
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Pure normalization of a persisted <see cref="LyricsPreferencesState"/> against the currently
/// registered providers. No WinUI/IO. See design §Reconcile.
/// </summary>
public static class LyricsPreferencesReconciler
{
    public const int AutoLoadMinMs = 300;
    public const int AutoLoadMaxMs = 3000;
    public const int ManualRefreshMinMs = 1000;
    public const int ManualRefreshMaxMs = 15000;

    /// <param name="persisted">Loaded state, or null on first run.</param>
    /// <param name="registeredIdsInDefaultOrder">Registered provider ids in canonical default order
    /// (known-canonical first, then any extras). Doubles as the membership set.</param>
    public static LyricsPreferencesState Reconcile(
        LyricsPreferencesState? persisted,
        IReadOnlyList<string> registeredIdsInDefaultOrder,
        int defaultAutoLoadMs,
        int defaultManualRefreshMs)
    {
        var registered = new HashSet<string>(registeredIdsInDefaultOrder, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<LyricsProviderPreference>();

        // 1. Keep persisted rows still registered, in saved order; drop unknown/duplicate.
        if (persisted?.Providers is { } persistedRows)
        {
            foreach (var row in persistedRows)
            {
                if (row is null || string.IsNullOrEmpty(row.Id) || !registered.Contains(row.Id) || !seen.Add(row.Id))
                {
                    continue;
                }

                rows.Add(new LyricsProviderPreference { Id = row.Id, Enabled = row.Enabled });
            }
        }

        // 2. Append registered ids missing from the persisted list, enabled, in canonical order.
        foreach (var id in registeredIdsInDefaultOrder)
        {
            if (seen.Add(id))
            {
                rows.Add(new LyricsProviderPreference { Id = id, Enabled = true });
            }
        }

        // 3. At-least-one-enabled: enable the first entry if reconciliation left none.
        if (rows.Count > 0 && !rows.Any(p => p.Enabled))
        {
            rows[0].Enabled = true;
        }

        // 4. Enabled-first stable ordering: enabled entries keep their order, then disabled keep theirs.
        var ordered = rows.Where(p => p.Enabled).Concat(rows.Where(p => !p.Enabled)).ToList();

        return new LyricsPreferencesState
        {
            Providers = ordered,
            AutoLoadBudgetMs = ClampOrDefault(persisted?.AutoLoadBudgetMs, defaultAutoLoadMs, AutoLoadMinMs, AutoLoadMaxMs),
            ManualRefreshBudgetMs = ClampOrDefault(persisted?.ManualRefreshBudgetMs, defaultManualRefreshMs, ManualRefreshMinMs, ManualRefreshMaxMs),
        };
    }

    private static int ClampOrDefault(int? value, int fallback, int min, int max)
    {
        // Missing (first run) or a blank/zero record → default; otherwise clamp into range.
        if (value is null or 0)
        {
            return fallback;
        }

        return Math.Clamp(value.Value, min, max);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsPreferencesReconcilerTests"`
Expected: PASS (8 tests).

- [ ] **Step 6: Commit**

```bash
git add Hypersonic.Core/Models/Lyrics/LyricsProviderPreference.cs Hypersonic.Core/Models/Lyrics/LyricsPreferencesState.cs Hypersonic.Core/Services/Lyrics/LyricsPreferencesReconciler.cs Hypersonic.Core.Tests/Services/Lyrics/LyricsPreferencesReconcilerTests.cs
git commit -m "feat(lyrics): pure reconcile/clamp for provider preferences"
```

---

### Task 2: `ILyricsPreferences` contract + `LyricsPreferences` service (load + reads)

The WinUI singleton. This task implements load, reconcile-on-load, and the read surface + defaults; the five mutation methods throw `NotSupportedException` (implemented in Task 3). Also adds two shared test fakes and the DI registration.

**Files:**
- Create: `Hypersonic/Contracts/Services/ILyricsPreferences.cs`
- Create: `Hypersonic/Services/LyricsPreferences.cs`
- Create: `Hypersonic.Tests/Fakes/FakeLocalSettingsService.cs`
- Create: `Hypersonic.Tests/Fakes/StubLyricsProvider.cs`
- Create: `Hypersonic.Tests/Services/LyricsPreferencesTests.cs`
- Modify: `Hypersonic/App.xaml.cs` (register the singleton — near the other lyrics registrations around line 150)

**Interfaces:**
- Consumes: `ILocalSettingsService`, `IEnumerable<ILyricsProvider>` (`Id`, `DisplayName`), `IOptions<LyricsOrchestrationOptions>` (`DefaultFanOutBudgetMs`, `ManualRefreshBudgetMs`), `ILoggingService`, `LyricsPreferencesReconciler` (Task 1).
- Produces:
  - `record LyricsProviderView(string Id, string DisplayName, bool Enabled)`
  - `interface ILyricsPreferences` (see Step 1 for the full member list)

- [ ] **Step 1: Write the contract**

Create `Hypersonic/Contracts/Services/ILyricsPreferences.cs`:

```csharp
namespace Hypersonic.Contracts.Services;

/// <summary>A provider row as shown in Settings: id, brand name, and whether it is enabled.</summary>
public sealed record LyricsProviderView(string Id, string DisplayName, bool Enabled);

/// <summary>
/// Single source of truth for the user's lyrics provider preferences (enabled set + priority order)
/// and the two fan-out budgets. Consulted live by <c>LyricsService</c> and <c>NowPlayingViewModel</c>;
/// bound by the lyrics settings UI. Persists itself via <c>ILocalSettingsService</c>.
/// </summary>
public interface ILyricsPreferences
{
    /// <summary>Idempotent load + reconcile against the registered providers. Cheap after first call.</summary>
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);

    /// <summary>Enabled provider ids in priority order — the fan-out set.</summary>
    IReadOnlyList<string> EnabledProviderIds { get; }

    TimeSpan AutoLoadBudget { get; }
    TimeSpan ManualRefreshBudget { get; }

    /// <summary>Registered providers in the canonical default order (all-enabled baseline for reset).</summary>
    IReadOnlyList<string> DefaultProviderOrder { get; }
    TimeSpan DefaultAutoLoadBudget { get; }
    TimeSpan DefaultManualRefreshBudget { get; }

    /// <summary>All providers in enabled-first order, with display names, for the settings UI.</summary>
    IReadOnlyList<LyricsProviderView> GetProviders();

    void SetEnabled(string id, bool enabled);
    void Reorder(IReadOnlyList<string> orderedEnabledIds);
    void SetAutoLoadBudget(TimeSpan value);
    void SetManualRefreshBudget(TimeSpan value);
    void ResetProviders();
}
```

- [ ] **Step 2: Write the shared test fakes**

Create `Hypersonic.Tests/Fakes/FakeLocalSettingsService.cs`:

```csharp
using Hypersonic.Core.Contracts.Services;

namespace Hypersonic.Tests.Fakes;

/// <summary>In-memory ILocalSettingsService for tests. Stores the last value per key verbatim.</summary>
public sealed class FakeLocalSettingsService : ILocalSettingsService
{
    public Dictionary<string, object?> Store { get; } = [];
    public int WriteCount { get; private set; }

    public T? GetAs<T>(string key)
        => Store.TryGetValue(key, out var v) && v is not null ? (T)v : default;

    public Task<T?> ReadSettingAsync<T>(string key)
        => Task.FromResult(Store.TryGetValue(key, out var v) && v is not null ? (T?)v : default);

    public Task SaveSettingAsync<T>(string key, T value)
    {
        Store[key] = value;
        WriteCount++;
        return Task.CompletedTask;
    }

    public Task RemoveSettingAsync(string key)
    {
        Store.Remove(key);
        return Task.CompletedTask;
    }
}
```

Create `Hypersonic.Tests/Fakes/StubLyricsProvider.cs`:

```csharp
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Tests.Fakes;

/// <summary>A registered provider with an id + display name that never actually fetches. For
/// preferences/VM tests that only need the registered set, not real lyrics.</summary>
public sealed class StubLyricsProvider(string id, string displayName) : ILyricsProvider
{
    public string Id => id;
    public string DisplayName => displayName;

    public Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
        => throw new NotSupportedException("StubLyricsProvider does not fetch.");
}
```

- [ ] **Step 3: Write the failing load/read tests**

Create `Hypersonic.Tests/Services/LyricsPreferencesTests.cs`:

```csharp
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
    public void BeforeLoad_ReportsAllEnabledDefaults()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());

        CollectionAssert.AreEqual(
            new[] { "subsonic", "lrclib", "amll-ttml-db", "qq", "netease", "kugou" },
            prefs.EnabledProviderIds.ToArray());
        Assert.AreEqual(TimeSpan.FromMilliseconds(700), prefs.AutoLoadBudget);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5000), prefs.ManualRefreshBudget);
    }

    [TestMethod]
    public async Task Load_FirstRun_SeedsAllEnabledInDefaultOrder()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());

        await prefs.EnsureLoadedAsync();

        Assert.AreEqual(6, prefs.EnabledProviderIds.Count);
        CollectionAssert.AreEqual(prefs.DefaultProviderOrder.ToArray(), prefs.EnabledProviderIds.ToArray());
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
}
```

- [ ] **Step 4: Run to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsPreferencesTests"`
Expected: FAIL to compile — `LyricsPreferences` does not exist.

- [ ] **Step 5: Implement the service (load + reads; mutations stubbed)**

Create `Hypersonic/Services/LyricsPreferences.cs`:

```csharp
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services;

/// <summary>
/// Singleton owner of the lyrics provider preferences and the two fan-out budgets. Holds an
/// atomically-swapped immutable snapshot; persists itself under one <see cref="ILocalSettingsService"/>
/// key. See design §Pivotal decisions.
/// </summary>
public sealed class LyricsPreferences : ILyricsPreferences
{
    internal const string SettingsKey = "LyricsPreferences";

    // Canonical default order — pinned in code, not DI registration order (design §Order is enabled-first).
    private static readonly string[] CanonicalOrder =
    [
        Hypersonic.Services.Lyrics.SubsonicLyricsConstants.ProviderId,
        Hypersonic.Core.Services.Lyrics.LrcLibLyricsConstants.ProviderId,
        Hypersonic.Services.Lyrics.AmllLyricsConstants.ProviderId,
        Hypersonic.Core.Services.Lyrics.QQLyricsConstants.ProviderId,
        Hypersonic.Core.Services.Lyrics.NeteaseLyricsConstants.ProviderId,
        Hypersonic.Core.Services.Lyrics.KugouLyricsConstants.ProviderId,
    ];

    private readonly ILocalSettingsService _settings;
    private readonly Dictionary<string, string> _displayNames;
    private readonly string[] _registeredInDefaultOrder;
    private readonly int _defaultAutoMs;
    private readonly int _defaultManualMs;
    private readonly ILogger _log;
    private readonly object _loadLock = new();

    private volatile LyricsPreferencesState _snapshot;
    private Task? _loadTask;

    public LyricsPreferences(
        ILocalSettingsService settings,
        IEnumerable<ILyricsProvider> providers,
        IOptions<LyricsOrchestrationOptions> orchestrationOptions,
        ILoggingService logService)
    {
        _settings = settings;
        _displayNames = providers.ToDictionary(p => p.Id, p => p.DisplayName, StringComparer.Ordinal);
        _defaultAutoMs = orchestrationOptions.Value.DefaultFanOutBudgetMs;
        _defaultManualMs = orchestrationOptions.Value.ManualRefreshBudgetMs;
        _log = logService.GetLogger(nameof(LyricsPreferences));

        var canonicalSet = new HashSet<string>(CanonicalOrder, StringComparer.Ordinal);
        _registeredInDefaultOrder = CanonicalOrder.Where(_displayNames.ContainsKey)
            .Concat(_displayNames.Keys.Where(id => !canonicalSet.Contains(id)))
            .ToArray();

        // Defaults snapshot so reads never see uninitialized state before EnsureLoadedAsync.
        _snapshot = LyricsPreferencesReconciler.Reconcile(null, _registeredInDefaultOrder, _defaultAutoMs, _defaultManualMs);
    }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_loadLock)
        {
            return _loadTask ??= LoadCoreAsync();
        }
    }

    private async Task LoadCoreAsync()
    {
        try
        {
            var persisted = await _settings.ReadSettingAsync<LyricsPreferencesState>(SettingsKey).ConfigureAwait(false);
            _snapshot = LyricsPreferencesReconciler.Reconcile(persisted, _registeredInDefaultOrder, _defaultAutoMs, _defaultManualMs);
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to load lyrics preferences; using defaults", exception: ex);
            _snapshot = LyricsPreferencesReconciler.Reconcile(null, _registeredInDefaultOrder, _defaultAutoMs, _defaultManualMs);
        }
    }

    public IReadOnlyList<string> EnabledProviderIds
        => _snapshot.Providers.Where(p => p.Enabled).Select(p => p.Id).ToArray();

    public TimeSpan AutoLoadBudget => TimeSpan.FromMilliseconds(_snapshot.AutoLoadBudgetMs);
    public TimeSpan ManualRefreshBudget => TimeSpan.FromMilliseconds(_snapshot.ManualRefreshBudgetMs);

    public IReadOnlyList<string> DefaultProviderOrder => _registeredInDefaultOrder;
    public TimeSpan DefaultAutoLoadBudget => TimeSpan.FromMilliseconds(_defaultAutoMs);
    public TimeSpan DefaultManualRefreshBudget => TimeSpan.FromMilliseconds(_defaultManualMs);

    public IReadOnlyList<LyricsProviderView> GetProviders()
        => _snapshot.Providers
            .Select(p => new LyricsProviderView(p.Id, _displayNames.GetValueOrDefault(p.Id, p.Id), p.Enabled))
            .ToArray();

    // Mutations — implemented in Task 3.
    public void SetEnabled(string id, bool enabled) => throw new NotSupportedException();
    public void Reorder(IReadOnlyList<string> orderedEnabledIds) => throw new NotSupportedException();
    public void SetAutoLoadBudget(TimeSpan value) => throw new NotSupportedException();
    public void SetManualRefreshBudget(TimeSpan value) => throw new NotSupportedException();
    public void ResetProviders() => throw new NotSupportedException();
}
```

> Note: if the `SubsonicLyricsConstants` / `AmllLyricsConstants` namespace differs from `Hypersonic.Services.Lyrics`, adjust the fully-qualified references — the `ProviderId` *values* are fixed.

- [ ] **Step 6: Register in DI**

In `Hypersonic/App.xaml.cs`, immediately before the `ILyricsService` registration (`services.AddSingleton<ILyricsService, LyricsService>();`, ~line 150), add:

```csharp
            services.AddSingleton<ILyricsPreferences, LyricsPreferences>();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsPreferencesTests"`
Expected: PASS (5 tests).

- [ ] **Step 8: Commit**

```bash
git add Hypersonic/Contracts/Services/ILyricsPreferences.cs Hypersonic/Services/LyricsPreferences.cs Hypersonic/App.xaml.cs Hypersonic.Tests/Fakes/FakeLocalSettingsService.cs Hypersonic.Tests/Fakes/StubLyricsProvider.cs Hypersonic.Tests/Services/LyricsPreferencesTests.cs
git commit -m "feat(lyrics): ILyricsPreferences service with load + reconcile + reads"
```

---

### Task 3: `LyricsPreferences` mutations + persistence

Implements the five mutations (enabled-first invariant, ≥1 guard, budget clamping, serialized persistence) and a test hook.

**Files:**
- Modify: `Hypersonic/Services/LyricsPreferences.cs`
- Modify: `Hypersonic.Tests/Services/LyricsPreferencesTests.cs`

**Interfaces:**
- Produces (on `LyricsPreferences`): working `SetEnabled` / `Reorder` / `SetAutoLoadBudget` / `SetManualRefreshBudget` / `ResetProviders`, plus `internal Task WaitForPendingSavesAsync()`.

- [ ] **Step 1: Write the failing mutation tests**

Append these methods to `LyricsPreferencesTests` (inside the class):

```csharp
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
    public async Task ResetProviders_RestoresAllEnabledDefaultOrder_KeepsBudgets()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());
        await prefs.EnsureLoadedAsync();
        prefs.SetEnabled("subsonic", false);
        prefs.Reorder(["lrclib", "amll-ttml-db", "qq", "netease", "kugou"]);
        prefs.SetAutoLoadBudget(TimeSpan.FromSeconds(2));

        prefs.ResetProviders();

        CollectionAssert.AreEqual(prefs.DefaultProviderOrder.ToArray(), prefs.EnabledProviderIds.ToArray());
        Assert.AreEqual(6, prefs.EnabledProviderIds.Count);
        Assert.AreEqual(TimeSpan.FromSeconds(2), prefs.AutoLoadBudget, "reset providers leaves budgets alone");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsPreferencesTests"`
Expected: FAIL — the mutation methods throw `NotSupportedException` and `WaitForPendingSavesAsync` does not exist.

- [ ] **Step 3: Implement the mutations + persistence**

In `Hypersonic/Services/LyricsPreferences.cs`, add these fields beside the others:

```csharp
    private readonly object _saveLock = new();
    private Task _saveQueue = Task.CompletedTask;
```

Replace the five stubbed mutation methods with:

```csharp
    public void SetEnabled(string id, bool enabled)
    {
        var providers = Clone(_snapshot.Providers);
        var index = providers.FindIndex(p => p.Id == id);
        if (index < 0 || providers[index].Enabled == enabled)
        {
            return;
        }

        if (!enabled && providers.Count(p => p.Enabled) <= 1)
        {
            return; // at-least-one-enabled invariant
        }

        var item = providers[index];
        providers.RemoveAt(index);
        item.Enabled = enabled;

        if (enabled)
        {
            providers.Insert(providers.Count(p => p.Enabled), item); // end of enabled run
        }
        else
        {
            providers.Add(item); // sink to the bottom
        }

        Publish(providers, _snapshot.AutoLoadBudgetMs, _snapshot.ManualRefreshBudgetMs);
    }

    public void Reorder(IReadOnlyList<string> orderedEnabledIds)
    {
        var current = _snapshot;
        var byId = current.Providers.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var enabled = new List<LyricsProviderPreference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var providerId in orderedEnabledIds)
        {
            if (byId.TryGetValue(providerId, out var p) && p.Enabled && seen.Add(providerId))
            {
                enabled.Add(p);
            }
        }

        // Safety: any enabled provider missing from the requested order keeps its place at the end.
        foreach (var p in current.Providers.Where(p => p.Enabled))
        {
            if (seen.Add(p.Id))
            {
                enabled.Add(p);
            }
        }

        var disabled = current.Providers.Where(p => !p.Enabled);
        var providers = Clone(enabled.Concat(disabled).ToList());
        Publish(providers, current.AutoLoadBudgetMs, current.ManualRefreshBudgetMs);
    }

    public void SetAutoLoadBudget(TimeSpan value)
    {
        var ms = Math.Clamp((int)value.TotalMilliseconds, LyricsPreferencesReconciler.AutoLoadMinMs, LyricsPreferencesReconciler.AutoLoadMaxMs);
        Publish(Clone(_snapshot.Providers), ms, _snapshot.ManualRefreshBudgetMs);
    }

    public void SetManualRefreshBudget(TimeSpan value)
    {
        var ms = Math.Clamp((int)value.TotalMilliseconds, LyricsPreferencesReconciler.ManualRefreshMinMs, LyricsPreferencesReconciler.ManualRefreshMaxMs);
        Publish(Clone(_snapshot.Providers), _snapshot.AutoLoadBudgetMs, ms);
    }

    public void ResetProviders()
    {
        var providers = _registeredInDefaultOrder
            .Select(id => new LyricsProviderPreference { Id = id, Enabled = true })
            .ToList();
        Publish(providers, _snapshot.AutoLoadBudgetMs, _snapshot.ManualRefreshBudgetMs);
    }

    private static List<LyricsProviderPreference> Clone(IEnumerable<LyricsProviderPreference> source)
        => source.Select(p => new LyricsProviderPreference { Id = p.Id, Enabled = p.Enabled }).ToList();

    private void Publish(List<LyricsProviderPreference> providers, int autoMs, int manualMs)
    {
        _snapshot = new LyricsPreferencesState
        {
            Providers = providers,
            AutoLoadBudgetMs = autoMs,
            ManualRefreshBudgetMs = manualMs,
        };

        var state = _snapshot;
        lock (_saveLock)
        {
            _saveQueue = SaveAfterPreviousAsync(_saveQueue, state);
        }
    }

    private async Task SaveAfterPreviousAsync(Task previous, LyricsPreferencesState state)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch
        {
            // Keep later writes moving if an earlier one failed.
        }

        try
        {
            await _settings.SaveSettingAsync(SettingsKey, state).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to persist lyrics preferences", exception: ex);
        }
    }

    internal Task WaitForPendingSavesAsync()
    {
        lock (_saveLock)
        {
            return _saveQueue;
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsPreferencesTests"`
Expected: PASS (12 tests total).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/Services/LyricsPreferences.cs Hypersonic.Tests/Services/LyricsPreferencesTests.cs
git commit -m "feat(lyrics): LyricsPreferences mutations + serialized persistence"
```

---

### Task 4: Rewire `LyricsService` to consult `ILyricsPreferences`

Order+filter the fan-out by the enabled/priority snapshot and source the auto budget from preferences. Drops the direct `IOptions<LyricsOrchestrationOptions>` dependency.

**Files:**
- Modify: `Hypersonic/Services/LyricsService.cs`
- Create: `Hypersonic.Tests/Fakes/FakeLyricsPreferences.cs`
- Modify: `Hypersonic.Tests/Services/Lyrics/LyricsServiceTests.cs`

**Interfaces:**
- Consumes: `ILyricsPreferences` (`EnsureLoadedAsync`, `EnabledProviderIds`, `AutoLoadBudget`).
- Produces (test fake): `FakeLyricsPreferences` implementing `ILyricsPreferences`, with settable `EnabledProviderIds`, `AutoLoadBudget`, `ManualRefreshBudget`.

- [ ] **Step 1: Write the shared preferences fake**

Create `Hypersonic.Tests/Fakes/FakeLyricsPreferences.cs`:

```csharp
using Hypersonic.Contracts.Services;

namespace Hypersonic.Tests.Fakes;

/// <summary>Configurable ILyricsPreferences for tests. Reads are settable; mutations record calls.</summary>
public sealed class FakeLyricsPreferences : ILyricsPreferences
{
    public IReadOnlyList<string> EnabledProviderIds { get; set; } = [];
    public TimeSpan AutoLoadBudget { get; set; } = TimeSpan.FromMilliseconds(700);
    public TimeSpan ManualRefreshBudget { get; set; } = TimeSpan.FromMilliseconds(5000);

    public IReadOnlyList<string> DefaultProviderOrder { get; set; } = [];
    public TimeSpan DefaultAutoLoadBudget { get; set; } = TimeSpan.FromMilliseconds(700);
    public TimeSpan DefaultManualRefreshBudget { get; set; } = TimeSpan.FromMilliseconds(5000);

    public List<LyricsProviderView> Providers { get; set; } = [];

    public int EnsureLoadedCount { get; private set; }
    public List<(string Id, bool Enabled)> SetEnabledCalls { get; } = [];
    public List<IReadOnlyList<string>> ReorderCalls { get; } = [];
    public int ResetProvidersCount { get; private set; }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        EnsureLoadedCount++;
        return Task.CompletedTask;
    }

    public IReadOnlyList<LyricsProviderView> GetProviders() => Providers;

    public void SetEnabled(string id, bool enabled) => SetEnabledCalls.Add((id, enabled));
    public void Reorder(IReadOnlyList<string> orderedEnabledIds) => ReorderCalls.Add(orderedEnabledIds);
    public void SetAutoLoadBudget(TimeSpan value) => AutoLoadBudget = value;
    public void SetManualRefreshBudget(TimeSpan value) => ManualRefreshBudget = value;

    public void ResetProviders()
    {
        ResetProvidersCount++;
        Providers = DefaultProviderOrder.Select(id => new LyricsProviderView(id, id, true)).ToList();
    }
}
```

- [ ] **Step 2: Write the failing rewire tests**

In `Hypersonic.Tests/Services/Lyrics/LyricsServiceTests.cs`, add these tests (they use the private `FakeLyricsProvider`, `LineContent`, and the `_store`/`_registry`/`_clientService`/`_time` fields already in the file):

```csharp
    [TestMethod]
    public async Task FanOut_DisabledProvider_IsNotFetched()
    {
        var subsonic = new FakeLyricsProvider(_ => PlainContent("subsonic")) { Id = "subsonic" };
        var lrclib = new FakeLyricsProvider(_ => LineContent("lrclib")) { Id = "lrclib" };
        var prefs = new FakeLyricsPreferences { EnabledProviderIds = ["lrclib"] }; // subsonic disabled
        var service = new LyricsService(
            _clientService, new ILyricsProvider[] { subsonic, lrclib }, _store, _registry,
            Options.Create(new LyricsCacheOptions()), prefs, _time, new NullLoggingService());

        var doc = await service.GetLyricsAsync(Query);

        Assert.AreEqual("lrclib", doc!.SourceProvider);
        Assert.AreEqual(0, subsonic.CallCount, "a disabled provider is never fetched");
        Assert.AreEqual(1, lrclib.CallCount);
    }

    [TestMethod]
    public async Task FanOut_Order_FollowsPreferences_NotRegistration()
    {
        // Equal tier → the earliest in preference order wins the tiebreak.
        var subsonic = new FakeLyricsProvider(_ => LineContent("subsonic")) { Id = "subsonic" };
        var lrclib = new FakeLyricsProvider(_ => LineContent("lrclib")) { Id = "lrclib" };
        var prefs = new FakeLyricsPreferences { EnabledProviderIds = ["lrclib", "subsonic"] }; // lrclib first
        var service = new LyricsService(
            _clientService, new ILyricsProvider[] { subsonic, lrclib }, _store, _registry,
            Options.Create(new LyricsCacheOptions()), prefs, _time, new NullLoggingService());

        var doc = await service.GetLyricsAsync(Query);

        Assert.AreEqual("lrclib", doc!.SourceProvider, "preference order decides the equal-tier tiebreak");
    }

    [TestMethod]
    public async Task GetLyrics_AwaitsPreferencesLoad()
    {
        var provider = new FakeLyricsProvider(_ => LegacyContent("x")) { Id = "subsonic" };
        var prefs = new FakeLyricsPreferences { EnabledProviderIds = ["subsonic"] };
        var service = new LyricsService(
            _clientService, new ILyricsProvider[] { provider }, _store, _registry,
            Options.Create(new LyricsCacheOptions()), prefs, _time, new NullLoggingService());

        await service.GetLyricsAsync(Query);

        Assert.IsTrue(prefs.EnsureLoadedCount >= 1, "the service ensures preferences are loaded before fetching");
    }
```

- [ ] **Step 3: Update the test harness to the new ctor**

In `LyricsServiceTests`, replace the `CreateService(cacheOptions, orchestration, providers)` body so it builds a `FakeLyricsPreferences` from the providers instead of passing `IOptions<LyricsOrchestrationOptions>`:

```csharp
    private LyricsService CreateService(LyricsCacheOptions? cacheOptions, LyricsOrchestrationOptions? orchestration, params ILyricsProvider[] providers)
    {
        cacheOptions ??= new LyricsCacheOptions();
        orchestration ??= new LyricsOrchestrationOptions();
        var prefs = new FakeLyricsPreferences
        {
            EnabledProviderIds = providers.Select(p => p.Id).ToArray(),
            AutoLoadBudget = TimeSpan.FromMilliseconds(orchestration.DefaultFanOutBudgetMs),
            ManualRefreshBudget = TimeSpan.FromMilliseconds(orchestration.ManualRefreshBudgetMs),
        };
        return new LyricsService(
            _clientService,
            providers,
            _store,
            _registry,
            Options.Create(cacheOptions),
            prefs,
            _time,
            new NullLoggingService());
    }
```

Add `using Hypersonic.Tests.Fakes;` if not already present (it is). Then update the two direct `new LyricsService(...)` constructions in `CacheRead_OverlaysRowMetadata_WhenParsedDocumentLacksIt` and `CacheRead_PrefersParsedMetadata_OverRow`: replace their 6th argument `Options.Create(new LyricsOrchestrationOptions())` with `new FakeLyricsPreferences()` (empty enabled set — those tests read the cache and never fan out).

- [ ] **Step 4: Run to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsServiceTests"`
Expected: FAIL to compile — `LyricsService` ctor still takes `IOptions<LyricsOrchestrationOptions>`.

- [ ] **Step 5: Rewire `LyricsService`**

In `Hypersonic/Services/LyricsService.cs`:

Add `using Hypersonic.Contracts.Services;` (already present). Replace the field declarations `private readonly LyricsOrchestrationOptions _orchestrationOptions;` with:

```csharp
    private readonly ILyricsPreferences _preferences;
    private readonly Dictionary<string, ILyricsProvider> _providersById;
```

Change the constructor signature — replace the parameter `IOptions<LyricsOrchestrationOptions> orchestrationOptions` with `ILyricsPreferences preferences`, and in the body replace `_orchestrationOptions = orchestrationOptions.Value;` with:

```csharp
        _preferences = preferences;
        _providersById = _providers.ToDictionary(p => p.Id, StringComparer.Ordinal);
```

(Remove the now-unused `using Microsoft.Extensions.Options;` only if nothing else needs it — `IOptions<LyricsCacheOptions>` is still a parameter, so keep the using.)

In `GetLyricsAsync`, right after `var token = linkedCts.Token;` and inside the `try`, add:

```csharp
            await _preferences.EnsureLoadedAsync(token).ConfigureAwait(false);
```

Then replace the budget-resolution block:

```csharp
            var defaultBudgetMs = _orchestrationOptions.DefaultFanOutBudgetMs > 0 ? _orchestrationOptions.DefaultFanOutBudgetMs : 700;
            var resolvedBudget = budget ?? TimeSpan.FromMilliseconds(defaultBudgetMs);
            if (resolvedBudget < TimeSpan.Zero)
            {
                // A caller-supplied negative budget (incl. the -1ms "infinite" sentinel) would throw in Task.Delay
                // or disable truncation — fall back to the default wait window.
                resolvedBudget = TimeSpan.FromMilliseconds(defaultBudgetMs);
            }
```

with:

```csharp
            var resolvedBudget = budget ?? _preferences.AutoLoadBudget;
            if (resolvedBudget < TimeSpan.Zero)
            {
                // A caller-supplied negative budget (incl. the -1ms "infinite" sentinel) would throw in Task.Delay
                // or disable truncation — fall back to the preferred wait window.
                resolvedBudget = _preferences.AutoLoadBudget;
            }
```

In `FetchAndStoreAsync`, build the active provider set once and thread it through. Replace the body from the fan-out call onward:

```csharp
        var (completed, anyTruncated) = await RunFanOutAsync(query, budget, cancellationToken).ConfigureAwait(false);
```

with:

```csharp
        var activeProviders = BuildActiveProviders();
        var (completed, anyTruncated) = await RunFanOutAsync(query, activeProviders, budget, cancellationToken).ConfigureAwait(false);
```

and change the `WriteNegative` arm to pass the active set's lead id — replace:

```csharp
            LyricsCacheAction.WriteNegative when !anyTruncated => await WriteNegativeAsync(keyHash, cancellationToken).ConfigureAwait(false),
```

with:

```csharp
            LyricsCacheAction.WriteNegative when !anyTruncated => await WriteNegativeAsync(keyHash, activeProviders, cancellationToken).ConfigureAwait(false),
```

Add the helper (near the other private methods):

```csharp
    private ILyricsProvider[] BuildActiveProviders()
    {
        var ids = _preferences.EnabledProviderIds;
        var active = new List<ILyricsProvider>(ids.Count);
        foreach (var id in ids)
        {
            if (_providersById.TryGetValue(id, out var provider))
            {
                active.Add(provider);
            }
        }

        return active.ToArray();
    }
```

Change `RunFanOutAsync` to take the active set: replace its signature `private async Task<(...)> RunFanOutAsync(LyricsQuery query, TimeSpan budget, CancellationToken cancellationToken)` with:

```csharp
    private async Task<(IReadOnlyList<LyricsProviderResult> Completed, bool AnyTruncated)> RunFanOutAsync(
        LyricsQuery query, ILyricsProvider[] providers, TimeSpan budget, CancellationToken cancellationToken)
```

and inside it replace every `_providers` reference with the `providers` parameter (`var n = providers.Length;`, `providers[idx].FetchAsync`, `providers[idx].Id`).

Change `WriteNegativeAsync` to take the active set for the negative row's source label: replace its signature `private async Task<LyricsDocument?> WriteNegativeAsync(string keyHash, CancellationToken cancellationToken)` with:

```csharp
    private async Task<LyricsDocument?> WriteNegativeAsync(string keyHash, ILyricsProvider[] activeProviders, CancellationToken cancellationToken)
```

and inside it replace `SourceProvider = _providers.Length > 0 ? _providers[0].Id : string.Empty,` with:

```csharp
                SourceProvider = activeProviders.Length > 0 ? activeProviders[0].Id : string.Empty,
```

(The `Dispose` loop still iterates `_providers` — leave it unchanged.)

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsServiceTests"`
Expected: PASS (all existing fan-out/cache tests plus the 3 new tests).

- [ ] **Step 7: Commit**

```bash
git add Hypersonic/Services/LyricsService.cs Hypersonic.Tests/Fakes/FakeLyricsPreferences.cs Hypersonic.Tests/Services/Lyrics/LyricsServiceTests.cs
git commit -m "feat(lyrics): LyricsService orders + filters fan-out via preferences"
```

---

### Task 5: Rewire `NowPlayingViewModel` manual-refresh budget

Read the manual budget from `ILyricsPreferences` instead of `IOptions<LyricsOrchestrationOptions>`.

**Files:**
- Modify: `Hypersonic/ViewModels/NowPlayingViewModel.cs`
- Modify: `Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs`

**Interfaces:**
- Consumes: `ILyricsPreferences` (`EnsureLoadedAsync`, `ManualRefreshBudget`).

- [ ] **Step 1: Update the failing test**

In `Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs`, add `using Hypersonic.Tests.Fakes;` (present) and replace the `Opts` field with a `Prefs` field, then migrate **all three** construction sites off `Opts` — both `Build` overloads (the `Func<…>`-handler one at ~line 68 and the `FakeLyricsService`-instance one at ~line 71) and the inline `new NowPlayingViewModel(...)` in `LateEnqueue_AfterOnNavigatedFrom_DoesNotStartFetch` (~line 249). Each currently passes `Opts` as the 3rd ctor argument, so missing one is a compile break.

Replace the `Opts` field + both `Build` overloads with:

```csharp
    private static readonly FakeLyricsPreferences Prefs = new()
    {
        AutoLoadBudget = TimeSpan.FromMilliseconds(700),
        ManualRefreshBudget = TimeSpan.FromMilliseconds(5000),
    };

    private static NowPlayingViewModel Build(FakePlaybackService svc, Func<LyricsQuery, Task<LyricsDocument?>> handler, Action<Action>? enqueueOverride = null)
        => new(svc, new FakeLyricsService(handler), Prefs, new NullLoggingService(), new FakeSourceCatalog(), synchronous: true, enqueueOverride: enqueueOverride);

    private static NowPlayingViewModel Build(FakePlaybackService svc, FakeLyricsService service, Action<Action>? enqueueOverride = null)
        => new(svc, service, Prefs, new NullLoggingService(), new FakeSourceCatalog(), synchronous: true, enqueueOverride: enqueueOverride);
```

Then in `LateEnqueue_AfterOnNavigatedFrom_DoesNotStartFetch` (~line 249), replace the `Opts` argument in the inline `new NowPlayingViewModel(...)` with `Prefs`.
```
Remove the now-unused `using Microsoft.Extensions.Options;`. Then update `Refresh_ForceRefreshesWithManualBudget` — replace the budget assertion:

```csharp
        Assert.AreEqual(TimeSpan.FromMilliseconds(new LyricsOrchestrationOptions().ManualRefreshBudgetMs), service.LastBudget);
```

with:

```csharp
        Assert.AreEqual(Prefs.ManualRefreshBudget, service.LastBudget);
```

(Remove `using Hypersonic.Models;` only if nothing else references it — leave it if `LyricsOrchestrationOptions` is used elsewhere in the file; otherwise drop it.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~NowPlayingViewModelTests"`
Expected: FAIL to compile — `NowPlayingViewModel` ctors still take `IOptions<LyricsOrchestrationOptions>`.

- [ ] **Step 3: Rewire the view model**

In `Hypersonic/ViewModels/NowPlayingViewModel.cs`:

Replace the field `private readonly LyricsOrchestrationOptions _orchestrationOptions;` with:

```csharp
    private readonly ILyricsPreferences _preferences;
```

In **both** constructors, replace the parameter `IOptions<LyricsOrchestrationOptions> orchestrationOptions` with `ILyricsPreferences preferences`, and replace the body line `_orchestrationOptions = orchestrationOptions.Value;` with `_preferences = preferences;`.

In `RefreshLyricsAsync`, replace:

```csharp
        var generation = Interlocked.Increment(ref _lyricsGeneration);
        var budget = TimeSpan.FromMilliseconds(_orchestrationOptions.ManualRefreshBudgetMs);
```

with:

```csharp
        var generation = Interlocked.Increment(ref _lyricsGeneration);
        await _preferences.EnsureLoadedAsync(CancellationToken.None);
        var budget = _preferences.ManualRefreshBudget;
```

Remove `using Microsoft.Extensions.Options;` and `using Hypersonic.Models;` if they become unused (verify no other reference remains), and add `using Hypersonic.Contracts.Services;` if not already present (it is — `ILyricsService`/`ILyricsSourceCatalog` come from there).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~NowPlayingViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/ViewModels/NowPlayingViewModel.cs Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs
git commit -m "feat(lyrics): NowPlayingViewModel reads manual budget from preferences"
```

---

### Task 6: `LyricsSettingsViewModel` + item model

The settings-side logic: two collections, sink-to-bottom toggle, last-enabled lock, reorder-persist, sliders, three per-control resets. Independently testable against `FakeLyricsPreferences`.

**Files:**
- Create: `Hypersonic/ViewModels/LyricsProviderItem.cs`
- Create: `Hypersonic/ViewModels/LyricsSettingsViewModel.cs`
- Create: `Hypersonic.Tests/ViewModels/LyricsSettingsViewModelTests.cs`
- Modify: `Hypersonic/App.xaml.cs` (register the VM transient)

**Interfaces:**
- Consumes: `ILyricsPreferences`.
- Produces:
  - `LyricsProviderItem : ObservableObject { string Id; string DisplayName; bool IsEnabled; bool CanToggle }`
  - `LyricsSettingsViewModel` with `ObservableCollection<LyricsProviderItem> EnabledProviders` / `DisabledProviders`, `bool ShowDisabledDivider`, `bool IsInitialized`, `double AutoLoadSeconds` / `ManualRefreshSeconds`, `string AutoLoadText` / `ManualRefreshText`, `bool CanResetProviders` / `CanResetAutoLoad` / `CanResetManualRefresh`, `ICommand ResetProvidersCommand` / `ResetAutoLoadCommand` / `ResetManualRefreshCommand`, `Task InitializeAsync()`.

- [ ] **Step 1: Write the item model**

Create `Hypersonic/ViewModels/LyricsProviderItem.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hypersonic.ViewModels;

/// <summary>One row in the lyrics provider settings list.</summary>
public partial class LyricsProviderItem : ObservableObject
{
    public string Id { get; }
    public string DisplayName { get; }

    /// <summary>Two-way bound to the row checkbox.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>False locks the checkbox — used to forbid disabling the last enabled provider.</summary>
    [ObservableProperty]
    public partial bool CanToggle { get; set; }

    public LyricsProviderItem(string id, string displayName, bool isEnabled)
    {
        Id = id;
        DisplayName = displayName;
        IsEnabled = isEnabled;
        CanToggle = true;
    }
}
```

- [ ] **Step 2: Write the failing view-model tests**

Create `Hypersonic.Tests/ViewModels/LyricsSettingsViewModelTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsSettingsViewModelTests"`
Expected: FAIL to compile — `LyricsSettingsViewModel` does not exist.

- [ ] **Step 4: Implement the view model**

Create `Hypersonic/ViewModels/LyricsSettingsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;

namespace Hypersonic.ViewModels;

/// <summary>
/// Drives the "Lyrics" settings section. Two collections model the enabled (reorderable) and disabled
/// (toggle-only) groups; all state changes flow through <see cref="ILyricsPreferences"/>.
/// </summary>
public partial class LyricsSettingsViewModel : ObservableObject
{
    private const double Epsilon = 0.001;

    private readonly ILyricsPreferences _preferences;
    private bool _initializing = true;
    private bool _mutating;

    public ObservableCollection<LyricsProviderItem> EnabledProviders { get; } = [];
    public ObservableCollection<LyricsProviderItem> DisabledProviders { get; } = [];

    [ObservableProperty]
    public partial bool IsInitialized { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoLoadText))]
    [NotifyPropertyChangedFor(nameof(CanResetAutoLoad))]
    public partial double AutoLoadSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManualRefreshText))]
    [NotifyPropertyChangedFor(nameof(CanResetManualRefresh))]
    public partial double ManualRefreshSeconds { get; set; }

    public bool ShowDisabledDivider => DisabledProviders.Count > 0;
    public string AutoLoadText => $"{AutoLoadSeconds:0.0} s";
    public string ManualRefreshText => $"{ManualRefreshSeconds:0.0} s";

    public bool CanResetAutoLoad => Math.Abs(AutoLoadSeconds - _preferences.DefaultAutoLoadBudget.TotalSeconds) > Epsilon;
    public bool CanResetManualRefresh => Math.Abs(ManualRefreshSeconds - _preferences.DefaultManualRefreshBudget.TotalSeconds) > Epsilon;

    public bool CanResetProviders =>
        DisabledProviders.Count > 0 ||
        !EnabledProviders.Select(p => p.Id).SequenceEqual(_preferences.DefaultProviderOrder);

    public ICommand ResetProvidersCommand { get; }
    public ICommand ResetAutoLoadCommand { get; }
    public ICommand ResetManualRefreshCommand { get; }

    public LyricsSettingsViewModel(ILyricsPreferences preferences)
    {
        _preferences = preferences;
        EnabledProviders.CollectionChanged += OnEnabledCollectionChanged;

        ResetProvidersCommand = new RelayCommand(ResetProviders, () => CanResetProviders);
        ResetAutoLoadCommand = new RelayCommand(() => AutoLoadSeconds = _preferences.DefaultAutoLoadBudget.TotalSeconds, () => CanResetAutoLoad);
        ResetManualRefreshCommand = new RelayCommand(() => ManualRefreshSeconds = _preferences.DefaultManualRefreshBudget.TotalSeconds, () => CanResetManualRefresh);
    }

    public async Task InitializeAsync()
    {
        await _preferences.EnsureLoadedAsync();

        _initializing = true;
        try
        {
            RebuildCollections();
            AutoLoadSeconds = _preferences.AutoLoadBudget.TotalSeconds;
            ManualRefreshSeconds = _preferences.ManualRefreshBudget.TotalSeconds;
        }
        finally
        {
            _initializing = false;
        }

        IsInitialized = true;
    }

    private void RebuildCollections()
    {
        _mutating = true;
        try
        {
            foreach (var item in EnabledProviders)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }

            foreach (var item in DisabledProviders)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }

            EnabledProviders.Clear();
            DisabledProviders.Clear();

            foreach (var view in _preferences.GetProviders())
            {
                var item = new LyricsProviderItem(view.Id, view.DisplayName, view.Enabled);
                item.PropertyChanged += OnItemPropertyChanged;
                (view.Enabled ? EnabledProviders : DisabledProviders).Add(item);
            }
        }
        finally
        {
            _mutating = false;
        }

        RecomputeToggleLocks();
        RaiseListDerivedChanged();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_initializing || _mutating || sender is not LyricsProviderItem item || e.PropertyName != nameof(LyricsProviderItem.IsEnabled))
        {
            return;
        }

        _mutating = true;
        try
        {
            if (!item.IsEnabled)
            {
                // Guard: never disable the last enabled provider (defensive; the checkbox is also locked).
                if (EnabledProviders.Count <= 1)
                {
                    item.IsEnabled = true;
                    return;
                }

                EnabledProviders.Remove(item);
                DisabledProviders.Add(item); // sink to the bottom
            }
            else
            {
                DisabledProviders.Remove(item);
                EnabledProviders.Add(item); // end of enabled run
            }

            _preferences.SetEnabled(item.Id, item.IsEnabled);
        }
        finally
        {
            _mutating = false;
        }

        RecomputeToggleLocks();
        RaiseListDerivedChanged();
    }

    private void OnEnabledCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_initializing || _mutating)
        {
            return;
        }

        // A drag-reorder inside the enabled ListView — persist the new priority order.
        _preferences.Reorder(EnabledProviders.Select(p => p.Id).ToArray());
        RecomputeToggleLocks();
        RaiseListDerivedChanged();
    }

    private void ResetProviders()
    {
        _preferences.ResetProviders();
        RebuildCollections();
    }

    private void RecomputeToggleLocks()
    {
        var canDisable = EnabledProviders.Count > 1;
        foreach (var item in EnabledProviders)
        {
            item.CanToggle = canDisable;
        }

        foreach (var item in DisabledProviders)
        {
            item.CanToggle = true;
        }
    }

    private void RaiseListDerivedChanged()
    {
        OnPropertyChanged(nameof(ShowDisabledDivider));
        OnPropertyChanged(nameof(CanResetProviders));
        (ResetProvidersCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnAutoLoadSecondsChanged(double value)
    {
        (ResetAutoLoadCommand as RelayCommand)?.NotifyCanExecuteChanged();
        if (_initializing)
        {
            return;
        }

        _preferences.SetAutoLoadBudget(TimeSpan.FromSeconds(value));
    }

    partial void OnManualRefreshSecondsChanged(double value)
    {
        (ResetManualRefreshCommand as RelayCommand)?.NotifyCanExecuteChanged();
        if (_initializing)
        {
            return;
        }

        _preferences.SetManualRefreshBudget(TimeSpan.FromSeconds(value));
    }
}
```

- [ ] **Step 5: Register the view model in DI**

In `Hypersonic/App.xaml.cs`, alongside the other transient ViewModel registrations (e.g. near `services.AddTransient<SettingsViewModel>();`), add:

```csharp
            services.AddTransient<LyricsSettingsViewModel>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsSettingsViewModelTests"`
Expected: PASS (8 tests).

- [ ] **Step 7: Commit**

```bash
git add Hypersonic/ViewModels/LyricsProviderItem.cs Hypersonic/ViewModels/LyricsSettingsViewModel.cs Hypersonic/App.xaml.cs Hypersonic.Tests/ViewModels/LyricsSettingsViewModelTests.cs
git commit -m "feat(lyrics): LyricsSettingsViewModel with two-collection provider list"
```

---

### Task 7: Settings UI — the "Lyrics" section

Add the section XAML, wire `LyricsViewModel` into the page, and add localized strings. UI wiring is verified by build + a manual visual check (no unit test — WinUI XAML rendering isn't unit-testable here).

**Files:**
- Modify: `Hypersonic/Views/SettingsPage.xaml`
- Modify: `Hypersonic/Views/SettingsPage.xaml.cs`
- Modify: `Hypersonic/Strings/en-us/Resources.resw`

**Interfaces:**
- Consumes: `LyricsSettingsViewModel` (Task 6) via `App.GetService<LyricsSettingsViewModel>()`.

- [ ] **Step 1: Add the localized strings**

In `Hypersonic/Strings/en-us/Resources.resw`, add these `<data>` entries (anywhere among the existing entries):

```xml
  <data name="Settings_Lyrics.Text" xml:space="preserve">
    <value>Lyrics</value>
  </data>
  <data name="Settings_LyricsProvidersCard.Header" xml:space="preserve">
    <value>Providers</value>
  </data>
  <data name="Settings_LyricsProvidersCard.Description" xml:space="preserve">
    <value>Drag to set priority. The best-synced result wins; order breaks ties. Unchecked sources are skipped.</value>
  </data>
  <data name="Settings_LyricsDisabledHeader.Text" xml:space="preserve">
    <value>Disabled</value>
  </data>
  <data name="Settings_LyricsAutoLoadCard.Header" xml:space="preserve">
    <value>Auto-load wait</value>
  </data>
  <data name="Settings_LyricsAutoLoadCard.Description" xml:space="preserve">
    <value>How long a track change waits for lyrics before deciding.</value>
  </data>
  <data name="Settings_LyricsManualRefreshCard.Header" xml:space="preserve">
    <value>Manual refresh wait</value>
  </data>
  <data name="Settings_LyricsManualRefreshCard.Description" xml:space="preserve">
    <value>The longer window used when you press refresh.</value>
  </data>
```

(The reset buttons reuse the existing `Settings_ResetTooltip` string via code-behind.)

- [ ] **Step 2: Add the section XAML**

In `Hypersonic/Views/SettingsPage.xaml`, add `xmlns:vm="using:Hypersonic.ViewModels"` to the `<Page>` element's namespace declarations. Then insert this block inside the `ContentArea` `StackPanel`, immediately before the `<!-- Scrobbling -->` comment:

```xml
            <!-- Lyrics -->
            <TextBlock x:Uid="Settings_Lyrics"
                       Style="{ThemeResource SubtitleTextBlockStyle}"
                       Margin="{StaticResource MediumTopMargin}" />

            <controls:SettingsCard
                x:Uid="Settings_LyricsProvidersCard"
                ContentAlignment="Vertical"
                Margin="{StaticResource XSmallTopMargin}">
                <controls:SettingsCard.HeaderIcon>
                    <FontIcon Glyph="&#xE8D6;" />
                </controls:SettingsCard.HeaderIcon>
                <Grid ColumnDefinitions="*,Auto" ColumnSpacing="8">
                    <StackPanel Grid.Column="0" Spacing="4">
                        <ListView
                            x:Name="EnabledProvidersList"
                            ItemsSource="{x:Bind LyricsViewModel.EnabledProviders, Mode=OneWay}"
                            SelectionMode="None"
                            CanReorderItems="True"
                            AllowDrop="True"
                            ReorderMode="Enabled">
                            <ListView.ItemContainerStyle>
                                <Style TargetType="ListViewItem">
                                    <Setter Property="Padding" Value="4,2" />
                                    <Setter Property="MinHeight" Value="0" />
                                </Style>
                            </ListView.ItemContainerStyle>
                            <ListView.ItemTemplate>
                                <DataTemplate x:DataType="vm:LyricsProviderItem">
                                    <Grid ColumnDefinitions="Auto,Auto,*" ColumnSpacing="10">
                                        <FontIcon Grid.Column="0" Glyph="&#xE76F;" FontSize="14" Opacity="0.6" VerticalAlignment="Center" />
                                        <CheckBox Grid.Column="1"
                                                  MinWidth="0"
                                                  IsChecked="{x:Bind IsEnabled, Mode=TwoWay}"
                                                  IsEnabled="{x:Bind CanToggle, Mode=OneWay}" />
                                        <TextBlock Grid.Column="2" Text="{x:Bind DisplayName}" VerticalAlignment="Center" />
                                    </Grid>
                                </DataTemplate>
                            </ListView.ItemTemplate>
                        </ListView>

                        <TextBlock x:Uid="Settings_LyricsDisabledHeader"
                                   Style="{ThemeResource CaptionTextBlockStyle}"
                                   Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                                   Margin="4,4,0,0"
                                   Visibility="{x:Bind LyricsViewModel.ShowDisabledDivider, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}" />

                        <ItemsControl ItemsSource="{x:Bind LyricsViewModel.DisabledProviders, Mode=OneWay}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="vm:LyricsProviderItem">
                                    <Grid ColumnDefinitions="Auto,Auto,*" ColumnSpacing="10" Padding="4,4" Opacity="0.5">
                                        <FontIcon Grid.Column="0" Glyph="&#xE76F;" FontSize="14" Opacity="0" VerticalAlignment="Center" />
                                        <CheckBox Grid.Column="1"
                                                  MinWidth="0"
                                                  IsChecked="{x:Bind IsEnabled, Mode=TwoWay}"
                                                  IsEnabled="{x:Bind CanToggle, Mode=OneWay}" />
                                        <TextBlock Grid.Column="2" Text="{x:Bind DisplayName}" VerticalAlignment="Center" />
                                    </Grid>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>

                    <Button
                        Grid.Column="1"
                        x:Name="ResetLyricsProvidersButton"
                        Style="{ThemeResource SubtleButtonStyle}"
                        Command="{x:Bind LyricsViewModel.ResetProvidersCommand}"
                        VerticalAlignment="Top"
                        HorizontalContentAlignment="Center"
                        VerticalContentAlignment="Center"
                        MinHeight="0"
                        MinWidth="32"
                        Width="32"
                        Height="32"
                        Padding="0">
                        <FontIcon Glyph="&#xE777;" FontSize="14" />
                    </Button>
                </Grid>
            </controls:SettingsCard>

            <controls:SettingsCard
                x:Uid="Settings_LyricsAutoLoadCard"
                Description="{x:Bind LyricsViewModel.AutoLoadText, Mode=OneWay}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Slider Minimum="0.3" Maximum="3.0" StepFrequency="0.1"
                            Value="{x:Bind LyricsViewModel.AutoLoadSeconds, Mode=TwoWay}"
                            Width="200" />
                    <Button
                        x:Name="ResetLyricsAutoLoadButton"
                        Style="{ThemeResource SubtleButtonStyle}"
                        Command="{x:Bind LyricsViewModel.ResetAutoLoadCommand}"
                        MinWidth="32" Width="32" Height="32" Padding="0">
                        <FontIcon Glyph="&#xE777;" FontSize="14" />
                    </Button>
                </StackPanel>
            </controls:SettingsCard>

            <controls:SettingsCard
                x:Uid="Settings_LyricsManualRefreshCard"
                Description="{x:Bind LyricsViewModel.ManualRefreshText, Mode=OneWay}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Slider Minimum="1.0" Maximum="15.0" StepFrequency="0.5"
                            Value="{x:Bind LyricsViewModel.ManualRefreshSeconds, Mode=TwoWay}"
                            Width="200" />
                    <Button
                        x:Name="ResetLyricsManualRefreshButton"
                        Style="{ThemeResource SubtleButtonStyle}"
                        Command="{x:Bind LyricsViewModel.ResetManualRefreshCommand}"
                        MinWidth="32" Width="32" Height="32" Padding="0">
                        <FontIcon Glyph="&#xE777;" FontSize="14" />
                    </Button>
                </StackPanel>
            </controls:SettingsCard>
```

- [ ] **Step 3: Wire the view model into the page**

In `Hypersonic/Views/SettingsPage.xaml.cs`, add the property beside `ViewModel`:

```csharp
    public LyricsSettingsViewModel LyricsViewModel
    {
        get;
    }
```

In the `SettingsPage()` constructor, after `ViewModel = App.GetService<SettingsViewModel>();`, add:

```csharp
        LyricsViewModel = App.GetService<LyricsSettingsViewModel>();
```

After `InitializeComponent();` and the existing `ApplyResetButtonAccessibility(...)` calls, add accessibility for the three new reset buttons and kick off the async load:

```csharp
        ApplyResetButtonAccessibility(ResetLyricsProvidersButton);
        ApplyResetButtonAccessibility(ResetLyricsAutoLoadButton);
        ApplyResetButtonAccessibility(ResetLyricsManualRefreshButton);

        _ = LyricsViewModel.InitializeAsync();
```

- [ ] **Step 4: Build the app to verify the XAML compiles**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: Build succeeds (XAML compiles, `x:Bind` paths resolve against `LyricsViewModel`).

- [ ] **Step 5: Run the full WinUI test suite**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS (all tests, including the earlier tasks').

- [ ] **Step 6: Manual visual check**

Launch the app (Visual Studio F5 or deploy the MSIX), open **Settings → Lyrics**, and confirm:
- Six providers listed, all enabled, in canonical order; drag by the grip reorders them.
- Unchecking a provider sinks it below the "Disabled" divider (grayed); its checkbox stays usable; re-checking returns it to the bottom of the enabled group.
- With one provider left enabled, its checkbox is locked.
- The two sliders show a live "x.x s" readout; each reset icon restores its default and disables when already at default.
- Reopen Settings (or relaunch) — choices persisted.

- [ ] **Step 7: Commit**

```bash
git add Hypersonic/Views/SettingsPage.xaml Hypersonic/Views/SettingsPage.xaml.cs Hypersonic/Strings/en-us/Resources.resw
git commit -m "feat(lyrics): add Lyrics provider settings section to Settings page"
```

---

## Self-Review

**Spec coverage:**
- Enable/disable per provider → Tasks 3 (`SetEnabled` + ≥1 guard), 6 (toggle-move), 7 (checkbox). ✅
- Drag-and-drop priority order → Tasks 3 (`Reorder`), 4 (fan-out order), 6 (`CollectionChanged` persist), 7 (reorderable `ListView`). ✅
- Two adjustable budgets (sliders in seconds) → Tasks 3 (clamp + persist), 5 (manual budget), 6 (slider props), 7 (sliders). ✅
- Persistence (MSIX + unpackaged, key `"LyricsPreferences"`) → Tasks 2–3 (`ILocalSettingsService`). ✅
- Live apply → Tasks 4–5 (per-fetch snapshot read). ✅
- Priority = tiebreak (policy untouched) → Task 4 (order feeds existing `LyricsFallbackPolicy`); no policy edit anywhere. ✅
- Sink-to-bottom disabled + handle-only affordance → Tasks 6 (two collections), 7 (grip + separate lists). ✅
- Three inline per-control resets → Tasks 6 (commands + `CanReset`), 7 (three reset buttons). ✅
- Explicit canonical default order → Tasks 2 (`CanonicalOrder`), 3 (`ResetProviders`). ✅
- At-least-one-enabled → Tasks 1 (reconcile), 3 (`SetEnabled` guard), 6 (`CanToggle` lock). ✅
- Reconcile robustness (drop unknown, append new) → Task 1. ✅

**Type consistency:** `ILyricsPreferences` members (`EnsureLoadedAsync`, `EnabledProviderIds`, `AutoLoadBudget`, `ManualRefreshBudget`, `DefaultProviderOrder`, `DefaultAutoLoadBudget`, `DefaultManualRefreshBudget`, `GetProviders`, `SetEnabled`, `Reorder`, `SetAutoLoadBudget`, `SetManualRefreshBudget`, `ResetProviders`) match across the contract (Task 2), the service (Tasks 2–3), the fake (Task 4), and both consumers (Tasks 4–6). `LyricsProviderItem` members (`Id`, `DisplayName`, `IsEnabled`, `CanToggle`) match between Task 6's model, VM, and Task 7's `x:Bind`. `Reconcile` signature matches between Task 1's definition and Task 2's two call sites.

**Note on the design doc:** the design said "SettingsViewModel extended"; this plan instead adds a focused `LyricsSettingsViewModel` resolved by `SettingsPage` (Task 6–7), preserving the design intent (a bound "Lyrics" section) while keeping the already-large `SettingsViewModel` and its three test files untouched.
