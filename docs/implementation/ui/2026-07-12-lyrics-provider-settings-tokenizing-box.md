# Lyrics Provider Settings — Tokenizing-Box Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the stacked two-group checkbox provider list in Settings → Lyrics with a single tokenizing chip box (chips = enabled providers in priority order; autosuggest to add/enable; `×` to remove/disable; drag to reorder), and retune the first-run/reset default to QQ → Subsonic → LrcLib.

**Architecture:** A thin `ReorderableTokenizingTextBox : TokenizingTextBox` subclass adds drag-to-reorder by mutating the *bound* collection (Remove+Insert), which TTB re-projects — no fork, no `CanReorderItems`. Chips are **`LyricsProviderItem` objects** (stable `Id` + `DisplayName`), rendered via `TextMemberPath="DisplayName"`; typed text is resolved to the domain object in `TokenItemAdding` via `args.Item`. Enable/disable route through TTB's user-gesture token events (`TokenItemAdded`/`Removed`), which do **not** fire on programmatic mutation, so reorder and enable/disable never collide. Separately, the pure `LyricsPreferencesReconciler` and `LyricsPreferences` seed an opinionated default-enabled set expressed as an ordered list.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.2.0), CommunityToolkit.WinUI.Controls.TokenizingTextBox 8.2.251219, CommunityToolkit.Mvvm 8.4.2, MSTest.

## Global Constraints

- **Provider ids** (exact, ordinal): `subsonic`, `lrclib`, `amll-ttml-db`, `qq`, `netease`, `kugou`. Id constants: `SubsonicLyricsConstants.ProviderId` / `AmllLyricsConstants.ProviderId` (in `Hypersonic.Services.Lyrics`); `LrcLibLyricsConstants` / `QQLyricsConstants` / `NeteaseLyricsConstants` / `KugouLyricsConstants` `.ProviderId` (in `Hypersonic.Core.Services.Lyrics`).
- **Chips are `LyricsProviderItem` objects** (`Id` + `DisplayName`), NOT strings — identity is by object, never presentation text. `TextMemberPath="DisplayName"`; `TokenItemAdding` resolves typed text to the candidate object and assigns `args.Item` (or cancels). This is the spec's model.
- **Default-enabled set, in priority order:** `qq` → `subsonic` → `lrclib`. AMLL / NetEase / Kugou ship **disabled**. Expressed as an **ordered list** passed to the reconciler; the pinned `CanonicalOrder` membership constant is left unchanged and distinct.
- **Reorder rule:** owned by an explicit `Drop` handler applied as **Remove + Insert on the bound collection**. Never set `CanReorderItems`. The drop index is resolved by a **row-aware** pure helper.
- **Enable/disable rule:** committed in the post-gesture events `TokenItemAdded` / `TokenItemRemoved` (which fire only on genuine user gestures, never on programmatic mutation). `TokenItemAdding` validates + assigns identity; `TokenItemRemoving` enforces the last-chip block. A newly-enabled chip is **normalized to the end** of the bound collection so its visual order matches the service's append-to-end.
- **At-least-one-enabled invariant:** the sole remaining chip cannot be removed (`TokenItemRemoving` cancels when `!CanRemove`); `ILyricsPreferences.SetEnabled` is the backstop.
- **Existing installs keep their persisted choices;** the new default only affects a fresh install or explicit reset.
- **Commits:** conventional, scope `lyrics`.
- **Build/test commands:**
  - Core tests: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`
  - App tests: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
  - Build app: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
  - Single test: append `--filter "FullyQualifiedName~TestName"`.

---

## File Structure

**Created:**
- `Hypersonic.Core/Helpers/ReorderDropIndex.cs` — pure, **row-aware** insertion-index math for a chip strip (WinUI-free, `double`-based).
- `Hypersonic.Core.Tests/Helpers/ReorderDropIndexTests.cs` — unit tests (single-row, wrapped-row, single-chip, empty).
- `Hypersonic/Controls/ReorderableTokenizingTextBox.cs` — TTB subclass adding drag-to-reorder + an `ItemsReordered` event. Type-agnostic (operates on `ItemsSource` as `IList`).

**Modified:**
- `Hypersonic.Core/Services/Lyrics/LyricsPreferencesReconciler.cs` — `Reconcile` gains an **ordered** `defaultEnabledInPriorityOrder` list; appends default-enabled first (enabled), then the rest disabled. `CanonicalOrder` semantics untouched.
- `Hypersonic.Core.Tests/Services/Lyrics/LyricsPreferencesReconcilerTests.cs` — revise the default-seeding tests.
- `Hypersonic/Contracts/Services/ILyricsPreferences.cs` — add `DefaultEnabledProviderIds`.
- `Hypersonic/Services/LyricsPreferences.cs` — add `DefaultEnabledOrder` (ordered); keep `CanonicalOrder`/`_registeredInDefaultOrder` as today; pass the ordered list to `Reconcile`; `ResetProviders` seeds it; expose `DefaultEnabledProviderIds`.
- `Hypersonic.Tests/Fakes/FakeLyricsPreferences.cs` — add `DefaultEnabledProviderIds`; `ResetProviders` seeds enabled+disabled **preserving each provider's DisplayName**.
- `Hypersonic.Tests/Services/LyricsPreferencesTests.cs` — revise first-run/reset expectations; add priority-order + append-disabled coverage.
- `Hypersonic/ViewModels/LyricsProviderItem.cs` — drop `IsEnabled`/`CanToggle`; keep immutable `Id` + `DisplayName`.
- `Hypersonic/ViewModels/LyricsSettingsViewModel.cs` — rewrite to the object-chip model: enabled `ObservableCollection<LyricsProviderItem>`, a VM-owned `ProviderSuggestions` collection, and explicit intents.
- `Hypersonic.Tests/ViewModels/LyricsSettingsViewModelTests.cs` — rewrite against the new VM surface.
- `Hypersonic/Views/SettingsPage.xaml` — replace the provider `SettingsCard` body (lines ~232–298) with the tokenizing box + templates.
- `Hypersonic/Views/SettingsPage.xaml.cs` — add provider token/suggestion/reorder handlers.
- `Hypersonic/Strings/en-us/Resources.resw` — add the provider-box placeholder string.

**No files deleted.** (`LyricsProviderItem` is retained, slimmed.)

---

## Task 1: Row-aware reorder drop-index helper

**Files:**
- Create: `Hypersonic.Core/Helpers/ReorderDropIndex.cs`
- Test: `Hypersonic.Core.Tests/Helpers/ReorderDropIndexTests.cs`

**Interfaces:**
- Produces: `public static class ReorderDropIndex { public static int Resolve(double pointerX, double pointerY, IReadOnlyList<(double Left, double Top, double Width, double Height)> chipBounds); }` — reading-order (row-major) insertion index in `[0, chipBounds.Count]`: the count of chips that precede the pointer (an earlier row, or the same row and left of the pointer). Empty → 0.

- [ ] **Step 1: Write the failing tests**

Create `Hypersonic.Core.Tests/Helpers/ReorderDropIndexTests.cs`:

```csharp
using Hypersonic.Core.Helpers;

namespace Hypersonic.Core.Tests.Helpers;

[TestClass]
public class ReorderDropIndexTests
{
    // One row: three 100-wide, 20-tall chips at x=0,100,200 (top=0). Midpoints x=50,150,250; row center y=10.
    private static readonly (double Left, double Top, double Width, double Height)[] OneRow =
        [(0, 0, 100, 20), (100, 0, 100, 20), (200, 0, 100, 20)];

    // Two rows: [A,B] on row 0 (top=0), [C] on row 1 (top=30). Row centers y=10 and y=40.
    private static readonly (double Left, double Top, double Width, double Height)[] TwoRows =
        [(0, 0, 100, 20), (100, 0, 100, 20), (0, 30, 100, 20)];

    [TestMethod]
    public void Empty_ReturnsZero()
        => Assert.AreEqual(0, ReorderDropIndex.Resolve(123, 45, []));

    [TestMethod]
    public void SingleRow_BeforeFirstMidpoint_ReturnsZero()
        => Assert.AreEqual(0, ReorderDropIndex.Resolve(40, 10, OneRow));

    [TestMethod]
    public void SingleRow_PastFirstMidpoint_ReturnsOne()
        => Assert.AreEqual(1, ReorderDropIndex.Resolve(60, 10, OneRow));

    [TestMethod]
    public void SingleRow_PastLastMidpoint_ReturnsCount()
        => Assert.AreEqual(3, ReorderDropIndex.Resolve(9999, 10, OneRow));

    [TestMethod]
    public void SingleRow_ExactlyAtMidpoint_CountsAsAtOrLeft()
        => Assert.AreEqual(1, ReorderDropIndex.Resolve(50, 10, OneRow));

    [TestMethod]
    public void SecondRow_BeforeItsChip_ReturnsCountOfRowOne()
        => Assert.AreEqual(2, ReorderDropIndex.Resolve(40, 35, TwoRows)); // both row-0 chips precede; row-1 chip does not

    [TestMethod]
    public void SecondRow_PastItsChip_ReturnsAll()
        => Assert.AreEqual(3, ReorderDropIndex.Resolve(60, 35, TwoRows));

    [TestMethod]
    public void SingleChip_LeftAndRight()
    {
        var one = new[] { (0d, 0d, 100d, 20d) };
        Assert.AreEqual(0, ReorderDropIndex.Resolve(40, 10, one));
        Assert.AreEqual(1, ReorderDropIndex.Resolve(60, 10, one));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~ReorderDropIndexTests"`
Expected: FAIL — `ReorderDropIndex` does not exist (build error).

- [ ] **Step 3: Write minimal implementation**

Create `Hypersonic.Core/Helpers/ReorderDropIndex.cs`:

```csharp
namespace Hypersonic.Core.Helpers;

/// <summary>
/// Pure, row-aware insertion-index math for a chip strip. Given a pointer (control-relative) and the realized
/// chips' bounds in reading order, returns the index at which a dropped chip should be inserted. A chip precedes
/// the pointer when it sits in an earlier row, or in the same row and left of the pointer. WinUI-free so it is
/// unit-testable; the control feeds it real container bounds.
/// </summary>
public static class ReorderDropIndex
{
    public static int Resolve(
        double pointerX,
        double pointerY,
        IReadOnlyList<(double Left, double Top, double Width, double Height)> chipBounds)
    {
        var index = 0;
        foreach (var (left, top, width, height) in chipBounds)
        {
            var centerX = left + (width / 2);
            var centerY = top + (height / 2);
            var rowTolerance = height / 2;

            var earlierRow = centerY < pointerY - rowTolerance;
            var sameRowAndLeft = System.Math.Abs(centerY - pointerY) <= rowTolerance && centerX <= pointerX;

            if (earlierRow || sameRowAndLeft)
            {
                index++;
            }
        }

        return index;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~ReorderDropIndexTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic.Core/Helpers/ReorderDropIndex.cs Hypersonic.Core.Tests/Helpers/ReorderDropIndexTests.cs
git commit -m "feat(lyrics): row-aware drop-index helper for chip reorder"
```

---

## Task 2: Companion default-enabled set (service layer)

Retune first-run/reset to QQ → Subsonic → LrcLib enabled; new providers append disabled; existing installs keep their state. `CanonicalOrder` membership order stays intact — priority is expressed by a separate ordered list.

**Files:**
- Modify: `Hypersonic.Core/Services/Lyrics/LyricsPreferencesReconciler.cs`
- Modify: `Hypersonic.Core.Tests/Services/Lyrics/LyricsPreferencesReconcilerTests.cs`
- Modify: `Hypersonic/Contracts/Services/ILyricsPreferences.cs`
- Modify: `Hypersonic/Services/LyricsPreferences.cs`
- Modify: `Hypersonic.Tests/Fakes/FakeLyricsPreferences.cs`
- Modify: `Hypersonic.Tests/Services/LyricsPreferencesTests.cs`
- Modify: `Hypersonic/ViewModels/LyricsSettingsViewModel.cs` (interim `CanResetProviders` only — the checkbox VM is still present until Task 4)
- Modify: `Hypersonic.Tests/ViewModels/LyricsSettingsViewModelTests.cs` (interim reset test only)

**Interfaces:**
- Consumes: `LyricsProviderPreference { string Id; bool Enabled; }`, `LyricsPreferencesState { List<LyricsProviderPreference> Providers; int AutoLoadBudgetMs; int ManualRefreshBudgetMs; }` (existing).
- Produces:
  - `LyricsPreferencesReconciler.Reconcile(LyricsPreferencesState? persisted, IReadOnlyList<string> registeredIdsInDefaultOrder, IReadOnlyList<string> defaultEnabledInPriorityOrder, int defaultAutoLoadMs, int defaultManualRefreshMs)`.
  - `ILyricsPreferences.DefaultEnabledProviderIds : IReadOnlyList<string>` — default-enabled ids in priority order (reset target).

- [ ] **Step 1: Adjust the failing reconciler tests**

In `Hypersonic.Core.Tests/Services/Lyrics/LyricsPreferencesReconcilerTests.cs`, add the ordered default list to the helper:

```csharp
    private static readonly string[] DefaultEnabled = ["qq", "subsonic", "lrclib"];

    private static LyricsPreferencesState Reconcile(LyricsPreferencesState? persisted)
        => LyricsPreferencesReconciler.Reconcile(persisted, Registered, DefaultEnabled, defaultAutoLoadMs: 700, defaultManualRefreshMs: 5000);
```

Replace `FirstRun_AllEnabled_DefaultOrder_DefaultBudgets` with:

```csharp
    [TestMethod]
    public void FirstRun_SeedsDefaultEnabledSet_InPriorityOrder_RestDisabled()
    {
        var state = Reconcile(null);

        CollectionAssert.AreEqual(
            new[] { "qq", "subsonic", "lrclib" },
            state.Providers.Where(p => p.Enabled).Select(p => p.Id).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "amll-ttml-db", "netease", "kugou" },
            state.Providers.Where(p => !p.Enabled).Select(p => p.Id).ToArray());
        Assert.IsTrue(state.Providers.Take(3).All(p => p.Enabled)); // enabled-first
        Assert.AreEqual(700, state.AutoLoadBudgetMs);
        Assert.AreEqual(5000, state.ManualRefreshBudgetMs);
    }
```

Replace `NewRegistered_Ids_Appended_Enabled_InCanonicalOrder` with:

```csharp
    [TestMethod]
    public void NewRegistered_Ids_Appended_EnabledOnlyIfInDefaultSet()
    {
        // Persisted knows only subsonic (enabled). The rest are appended: qq/lrclib enabled (default set),
        // amll/netease/kugou disabled.
        var persisted = new LyricsPreferencesState
        {
            Providers = [new() { Id = "subsonic", Enabled = true }],
            AutoLoadBudgetMs = 700,
            ManualRefreshBudgetMs = 5000,
        };

        var state = Reconcile(persisted);

        Assert.IsTrue(state.Providers.Single(p => p.Id == "subsonic").Enabled);
        Assert.IsTrue(state.Providers.Single(p => p.Id == "qq").Enabled);
        Assert.IsTrue(state.Providers.Single(p => p.Id == "lrclib").Enabled);
        Assert.IsFalse(state.Providers.Single(p => p.Id == "amll-ttml-db").Enabled);
        Assert.IsFalse(state.Providers.Single(p => p.Id == "netease").Enabled);
        Assert.IsFalse(state.Providers.Single(p => p.Id == "kugou").Enabled);
    }

    [TestMethod]
    public void ExistingAllEnabled_PersistedState_Preserved()
    {
        // Old installs that persisted all six enabled keep every provider enabled, in saved order —
        // the leaner default only applies to a fresh install or explicit reset (design §Companion change).
        var persisted = new LyricsPreferencesState
        {
            Providers = Registered.Select(id => new LyricsProviderPreference { Id = id, Enabled = true }).ToList(),
            AutoLoadBudgetMs = 700,
            ManualRefreshBudgetMs = 5000,
        };

        var state = Reconcile(persisted);

        Assert.IsTrue(state.Providers.All(p => p.Enabled));
        CollectionAssert.AreEqual(Registered, state.Providers.Select(p => p.Id).ToArray());
    }
```

(The remaining tests — `EnabledFirst_Ordering_IsEnforced`, `AllDisabledPersisted_EnablesFirstEntry`, `Budget_*`, `RoundTrip_*`, `Unknown_Ids_Dropped` — call the updated `Reconcile` helper unchanged and keep their assertions.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsPreferencesReconcilerTests"`
Expected: FAIL — `Reconcile` has no 5-arg overload (build error).

- [ ] **Step 3: Add the ordered default list to the reconciler**

In `Hypersonic.Core/Services/Lyrics/LyricsPreferencesReconciler.cs`, change the signature and split step 2 into 2a (default-enabled first) + 2b (rest, disabled):

```csharp
    /// <param name="registeredIdsInDefaultOrder">Registered provider ids in canonical membership order
    /// (known-canonical first, then extras). Doubles as the membership set for placing disabled providers.</param>
    /// <param name="defaultEnabledInPriorityOrder">Ids enabled by default, in priority order — applied to
    /// newly-appended ids (first run and never-before-seen upgrades). Existing persisted rows are untouched.</param>
    public static LyricsPreferencesState Reconcile(
        LyricsPreferencesState? persisted,
        IReadOnlyList<string> registeredIdsInDefaultOrder,
        IReadOnlyList<string> defaultEnabledInPriorityOrder,
        int defaultAutoLoadMs,
        int defaultManualRefreshMs)
    {
```

Replace the existing step-2 loop with:

```csharp
        // 2a. Append default-enabled ids missing from the persisted list, in priority order, enabled.
        foreach (var id in defaultEnabledInPriorityOrder)
        {
            if (registered.Contains(id) && seen.Add(id))
            {
                rows.Add(new LyricsProviderPreference { Id = id, Enabled = true });
            }
        }

        // 2b. Append any remaining registered ids, in canonical order, disabled by default
        //     (a new/opinionated-off provider appears but stays out of the fan-out until the user enables it).
        foreach (var id in registeredIdsInDefaultOrder)
        {
            if (seen.Add(id))
            {
                rows.Add(new LyricsProviderPreference { Id = id, Enabled = false });
            }
        }
```

- [ ] **Step 4: Run reconciler tests to verify they pass**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsPreferencesReconcilerTests"`
Expected: PASS.

- [ ] **Step 5: Commit the Core change**

```bash
git add Hypersonic.Core/Services/Lyrics/LyricsPreferencesReconciler.cs Hypersonic.Core.Tests/Services/Lyrics/LyricsPreferencesReconcilerTests.cs
git commit -m "feat(lyrics): reconciler seeds an ordered default-enabled set, appends new providers disabled"
```

- [ ] **Step 6: Add `DefaultEnabledProviderIds` to the contract**

In `Hypersonic/Contracts/Services/ILyricsPreferences.cs`, after `DefaultProviderOrder`:

```csharp
    /// <summary>Provider ids enabled by default, in priority order — the target of reset (§Companion change).</summary>
    IReadOnlyList<string> DefaultEnabledProviderIds { get; }
```

- [ ] **Step 7: Implement the default set in `LyricsPreferences`**

In `Hypersonic/Services/LyricsPreferences.cs`:

Add the ordered constant after `CanonicalOrder` (leave `CanonicalOrder` unchanged):

```csharp
    // Providers enabled by default, in priority order (QQ word-synced first; own server before community LrcLib).
    private static readonly string[] DefaultEnabledOrder =
    [
        Hypersonic.Core.Services.Lyrics.QQLyricsConstants.ProviderId,
        Hypersonic.Services.Lyrics.SubsonicLyricsConstants.ProviderId,
        Hypersonic.Core.Services.Lyrics.LrcLibLyricsConstants.ProviderId,
    ];
```

Add a field for the set:

```csharp
    private readonly HashSet<string> _defaultEnabledSet;
```

In the constructor, keep the existing `_registeredInDefaultOrder` build **unchanged**, and add the set + exposed list (place after the existing `_registeredInDefaultOrder` assignment):

```csharp
        _defaultEnabledSet = new HashSet<string>(DefaultEnabledOrder, StringComparer.Ordinal);
        DefaultEnabledProviderIds = DefaultEnabledOrder.Where(_displayNames.ContainsKey).ToArray();
```

Update the three `Reconcile(...)` call sites (ctor default snapshot, `LoadCoreAsync` success, `LoadCoreAsync` catch) to pass `DefaultEnabledProviderIds`, e.g.:

```csharp
        _snapshot = BuildSnapshot(LyricsPreferencesReconciler.Reconcile(null, _registeredInDefaultOrder, DefaultEnabledProviderIds, _defaultAutoMs, _defaultManualMs));
```

Add the exposed property near `DefaultProviderOrder`:

```csharp
    public IReadOnlyList<string> DefaultEnabledProviderIds { get; }
```

Rewrite `ResetProviders` to seed enabled (priority order) + disabled (rest, canonical order):

```csharp
    public void ResetProviders()
    {
        var enabled = DefaultEnabledProviderIds
            .Select(id => new LyricsProviderPreference { Id = id, Enabled = true });
        var disabled = _registeredInDefaultOrder
            .Where(id => !_defaultEnabledSet.Contains(id))
            .Select(id => new LyricsProviderPreference { Id = id, Enabled = false });
        Publish(enabled.Concat(disabled).ToList());
    }
```

- [ ] **Step 8: Update the fake (preserve DisplayNames on reset) and the interim VM `CanResetProviders`**

In `Hypersonic.Tests/Fakes/FakeLyricsPreferences.cs`, add the property:

```csharp
    public IReadOnlyList<string> DefaultEnabledProviderIds { get; set; } = [];
```

Replace `ResetProviders` so it seeds enabled+disabled **and keeps each provider's DisplayName** (mapped from the current `Providers`):

```csharp
    public void ResetProviders()
    {
        ResetProvidersCount++;
        var nameById = Providers.ToDictionary(p => p.Id, p => p.DisplayName, StringComparer.Ordinal);
        string Name(string id) => nameById.TryGetValue(id, out var n) ? n : id;

        var enabledSet = new HashSet<string>(DefaultEnabledProviderIds, StringComparer.Ordinal);
        var enabled = DefaultEnabledProviderIds.Select(id => new LyricsProviderView(id, Name(id), true));
        var disabled = DefaultProviderOrder.Where(id => !enabledSet.Contains(id))
            .Select(id => new LyricsProviderView(id, Name(id), false));
        Providers = enabled.Concat(disabled).ToList();
    }
```

In `Hypersonic/ViewModels/LyricsSettingsViewModel.cs` (still the checkbox model here), change `CanResetProviders`:

```csharp
    public bool CanResetProviders =>
        !EnabledProviders.Select(p => p.Id).SequenceEqual(_preferences.DefaultEnabledProviderIds);
```

- [ ] **Step 9: Revise the service + interim VM tests**

In `Hypersonic.Tests/Services/LyricsPreferencesTests.cs` (arrange via the existing `Create(new FakeLocalSettingsService(), AllProviders())` helper):

Update `BeforeLoad_ReportsAllEnabledDefaults` — the constructor's default snapshot now seeds only the default-enabled set — and rename it:

```csharp
    [TestMethod]
    public void BeforeLoad_ReportsDefaultEnabledSet()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());

        CollectionAssert.AreEqual(new[] { "qq", "subsonic", "lrclib" }, prefs.EnabledProviderIds.ToArray());
        Assert.AreEqual(TimeSpan.FromMilliseconds(700), prefs.AutoLoadBudget);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5000), prefs.ManualRefreshBudget);
    }
```

Replace `Load_FirstRun_SeedsAllEnabledInDefaultOrder`:

```csharp
    [TestMethod]
    public async Task Load_FirstRun_SeedsDefaultEnabledSet_InPriorityOrder()
    {
        var prefs = Create(new FakeLocalSettingsService(), AllProviders());

        await prefs.EnsureLoadedAsync();

        CollectionAssert.AreEqual(new[] { "qq", "subsonic", "lrclib" }, prefs.EnabledProviderIds.ToArray());
        CollectionAssert.AreEqual(new[] { "qq", "subsonic", "lrclib" }, prefs.DefaultEnabledProviderIds.ToArray());
    }
```

Replace `ResetProviders_RestoresAllEnabledDefaultOrder_KeepsBudgets` — **keep the budget regression check**:

```csharp
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
```

`Defaults_ExposeCanonicalOrderAndBudgets` is **unchanged** — `DefaultProviderOrder` stays the canonical order (review #3), so its assertion still holds.

In `Hypersonic.Tests/ViewModels/LyricsSettingsViewModelTests.cs`, set the default ids in the `Prefs()` factory and fix the reset test:

```csharp
            DefaultProviderOrder = ids,
            DefaultEnabledProviderIds = new[] { "qq", "subsonic", "lrclib" },
```

```csharp
    [TestMethod]
    public async Task ResetProviders_CallsPreferences_AndRebuilds()
    {
        var prefs = Prefs();
        var vm = await BuildAsync(prefs);
        vm.EnabledProviders.First(p => p.Id == "qq").IsEnabled = false;

        vm.ResetProvidersCommand.Execute(null);

        Assert.AreEqual(1, prefs.ResetProvidersCount);
        Assert.AreEqual(3, vm.EnabledProviders.Count);
        Assert.AreEqual(3, vm.DisabledProviders.Count);
        Assert.IsFalse(vm.CanResetProviders);
    }
```

> The `Prefs()` factory's `ids` array must include all six providers; if it seeds all-enabled `Providers`, the fake's reset now yields 3 enabled / 3 disabled, matching the assertion.

- [ ] **Step 10: Build and run the full suites**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64` → succeeds.
Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`
Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS. If `LyricsServiceTests` / `NowPlayingViewModelTests` assumed all-enabled defaults, fix them to set `EnabledProviderIds` explicitly on their `FakeLyricsPreferences`.

- [ ] **Step 11: Commit**

```bash
git add Hypersonic/Contracts/Services/ILyricsPreferences.cs Hypersonic/Services/LyricsPreferences.cs Hypersonic/ViewModels/LyricsSettingsViewModel.cs Hypersonic.Tests/Fakes/FakeLyricsPreferences.cs Hypersonic.Tests/Services/LyricsPreferencesTests.cs Hypersonic.Tests/ViewModels/LyricsSettingsViewModelTests.cs
git commit -m "feat(lyrics): default to QQ+Subsonic+LrcLib enabled; expose DefaultEnabledProviderIds"
```

---

## Task 3: `ReorderableTokenizingTextBox` control

**Files:**
- Create: `Hypersonic/Controls/ReorderableTokenizingTextBox.cs`

**Interfaces:**
- Consumes: `ReorderDropIndex.Resolve` (Task 1); `TokenizingTextBox` (base); `ItemsSource` bound to an `ObservableCollection<LyricsProviderItem>` (treated as `IList`).
- Produces: `public sealed partial class ReorderableTokenizingTextBox : TokenizingTextBox`, raising `public event TypedEventHandler<ReorderableTokenizingTextBox, IReadOnlyList<object>>? ItemsReordered` after a successful drag reorder, with the new full chip order. It never references view-model types — a chip is identified as "an item present in the bound `ItemsSource`" (the internal editor placeholder is not).

- [ ] **Step 1: Write the control**

Create `Hypersonic/Controls/ReorderableTokenizingTextBox.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.WinUI.Controls;

using Hypersonic.Core.Helpers;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Hypersonic.Controls;

/// <summary>
/// A <see cref="TokenizingTextBox"/> that adds drag-to-reorder over its chips. Reorder is owned explicitly:
/// the drop mutates the bound collection as a Remove+Insert (which the base re-projects, keeping the trailing
/// editor last) — <see cref="ListViewBase.CanReorderItems"/> is deliberately NOT used. A chip is identified as
/// an item present in the bound <see cref="ItemsControl.ItemsSource"/>; the internal editor placeholder is not,
/// so it can never be dragged or become a drop target.
/// </summary>
public sealed partial class ReorderableTokenizingTextBox : TokenizingTextBox
{
    private object? _dragged;

    public ReorderableTokenizingTextBox()
    {
        CanDragItems = true;
        AllowDrop = true;
        DragItemsStarting += OnDragItemsStarting;
        DragItemsCompleted += OnDragItemsCompleted;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    /// <summary>Raised after a drag reorder with the new full chip order (the bound item objects).</summary>
    public event TypedEventHandler<ReorderableTokenizingTextBox, IReadOnlyList<object>>? ItemsReordered;

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count == 1 && ItemsSource is IList source && source.Contains(e.Items[0]))
        {
            _dragged = e.Items[0];
        }
        else
        {
            _dragged = null;
            e.Cancel = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_dragged is not null)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_dragged is null || ItemsSource is not IList chips)
        {
            return;
        }

        e.Handled = true;

        var from = chips.IndexOf(_dragged);
        if (from < 0)
        {
            return;
        }

        var pointer = e.GetPosition(this);
        var bounds = new List<(double Left, double Top, double Width, double Height)>(chips.Count);
        foreach (var chip in chips)
        {
            if (ReferenceEquals(chip, _dragged))
            {
                continue;
            }

            if (ContainerFromItem(chip) is FrameworkElement fe)
            {
                var origin = fe.TransformToVisual(this).TransformPoint(new Point(0, 0));
                bounds.Add((origin.X, origin.Y, fe.ActualWidth, fe.ActualHeight));
            }
        }

        var target = ReorderDropIndex.Resolve(pointer.X, pointer.Y, bounds);
        target = System.Math.Clamp(target, 0, chips.Count - 1);

        if (target != from)
        {
            chips.RemoveAt(from);
            chips.Insert(target, _dragged);
            ItemsReordered?.Invoke(this, chips.Cast<object>().ToList());
        }
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Clear on every completion path — a successful internal drop, a cancel, or a drop outside this control.
        _dragged = null;
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: build succeeds (the control is not yet referenced by any XAML — fine).

- [ ] **Step 3: Commit**

```bash
git add Hypersonic/Controls/ReorderableTokenizingTextBox.cs
git commit -m "feat(lyrics): ReorderableTokenizingTextBox control (drag reorder via bound-collection Remove+Insert)"
```

> Drag behavior is verified in the running app in Task 4.

---

## Task 4: Tokenizing-box UI — item, VM, XAML, code-behind

Slim `LyricsProviderItem`, rewrite the VM to the object-chip model, swap the XAML block, wire the code-behind, and rewrite the VM tests. Lands as one deliverable (View/VM surfaces are coupled).

**Files:**
- Modify: `Hypersonic/ViewModels/LyricsProviderItem.cs`
- Modify: `Hypersonic/ViewModels/LyricsSettingsViewModel.cs`
- Modify: `Hypersonic/Views/SettingsPage.xaml`
- Modify: `Hypersonic/Views/SettingsPage.xaml.cs`
- Modify: `Hypersonic/Strings/en-us/Resources.resw`
- Modify: `Hypersonic.Tests/ViewModels/LyricsSettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `ILyricsPreferences` (`GetProviders()`, `SetEnabled`, `Reorder`, `ResetProviders`, `DefaultEnabledProviderIds`, budgets); `ReorderableTokenizingTextBox.ItemsReordered` (Task 3); `TokenItemAddingEventArgs.Item` / `TokenText`, `TokenItemAdded`/`Removed` (`object data`), `TokenItemRemovingEventArgs`.
- Produces (VM public surface):
  - `ObservableCollection<LyricsProviderItem> EnabledProviders` — TTB `ItemsSource` (priority order).
  - `ObservableCollection<LyricsProviderItem> ProviderSuggestions` — TTB `SuggestedItemsSource` (VM-owned, refreshed on every mutation incl. reset).
  - `bool IsInitialized`, `bool CanRemove`, `bool CanResetProviders`; budgets unchanged.
  - `Task InitializeAsync()`, `LyricsProviderItem? ResolveCandidate(string text)`, `void FilterSuggestions(string? query)`, `void EnableProvider(LyricsProviderItem item)`, `void DisableProvider(LyricsProviderItem item)`, `void CommitReorder(IReadOnlyList<LyricsProviderItem> orderedItems)`.

- [ ] **Step 1: Write the failing VM tests**

Rewrite `Hypersonic.Tests/ViewModels/LyricsSettingsViewModelTests.cs`:

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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsSettingsViewModelTests"`
Expected: FAIL — new VM members / `LyricsProviderItem` shape do not exist (build error).

- [ ] **Step 3: Slim `LyricsProviderItem`**

Replace the entire contents of `Hypersonic/ViewModels/LyricsProviderItem.cs`:

```csharp
namespace Hypersonic.ViewModels;

/// <summary>A lyrics provider as shown in the settings tokenizing box: a stable id plus its brand display name.</summary>
public sealed class LyricsProviderItem
{
    public string Id { get; }
    public string DisplayName { get; }

    public LyricsProviderItem(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}
```

- [ ] **Step 4: Rewrite the ViewModel**

Replace the entire contents of `Hypersonic/ViewModels/LyricsSettingsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;

namespace Hypersonic.ViewModels;

/// <summary>
/// Drives the "Lyrics" settings section. Enabled providers are chips (priority order) bound to the tokenizing box;
/// disabled providers are the autosuggest candidate pool. Enable/disable/reorder each route through an explicit
/// intent that commits to <see cref="ILyricsPreferences"/>.
/// </summary>
public partial class LyricsSettingsViewModel : ObservableObject
{
    private const double Epsilon = 0.001;

    private readonly ILyricsPreferences _preferences;
    private readonly List<LyricsProviderItem> _candidates = []; // disabled providers (source for suggestions)
    private bool _initializing = true;
    private bool _mutating;

    public ObservableCollection<LyricsProviderItem> EnabledProviders { get; } = [];
    public ObservableCollection<LyricsProviderItem> ProviderSuggestions { get; } = [];

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

    public bool CanRemove => EnabledProviders.Count > 1;
    public string AutoLoadText => $"{AutoLoadSeconds:0.0} s";
    public string ManualRefreshText => $"{ManualRefreshSeconds:0.0} s";

    public bool CanResetAutoLoad => Math.Abs(AutoLoadSeconds - _preferences.DefaultAutoLoadBudget.TotalSeconds) > Epsilon;
    public bool CanResetManualRefresh => Math.Abs(ManualRefreshSeconds - _preferences.DefaultManualRefreshBudget.TotalSeconds) > Epsilon;

    public bool CanResetProviders =>
        !EnabledProviders.Select(p => p.Id).SequenceEqual(_preferences.DefaultEnabledProviderIds);

    public ICommand ResetProvidersCommand { get; }
    public ICommand ResetAutoLoadCommand { get; }
    public ICommand ResetManualRefreshCommand { get; }

    public LyricsSettingsViewModel(ILyricsPreferences preferences)
    {
        _preferences = preferences;
        EnabledProviders.CollectionChanged += OnEnabledProvidersChanged;

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
            RebuildFromSnapshot();
            AutoLoadSeconds = _preferences.AutoLoadBudget.TotalSeconds;
            ManualRefreshSeconds = _preferences.ManualRefreshBudget.TotalSeconds;
        }
        finally
        {
            _initializing = false;
        }

        IsInitialized = true;
    }

    /// <summary>Exact-name (ordinal) match against the current disabled candidates — resolves typed token text
    /// into the stable provider item, or null to reject unknown/already-enabled text.</summary>
    public LyricsProviderItem? ResolveCandidate(string text)
        => _candidates.FirstOrDefault(c => string.Equals(c.DisplayName, text, StringComparison.Ordinal));

    /// <summary>Repopulates the visible suggestion list from the disabled candidates, filtered by query.</summary>
    public void FilterSuggestions(string? query)
    {
        ProviderSuggestions.Clear();
        var matches = string.IsNullOrWhiteSpace(query)
            ? _candidates
            : _candidates.Where(c => c.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        foreach (var candidate in matches)
        {
            ProviderSuggestions.Add(candidate);
        }
    }

    public void EnableProvider(LyricsProviderItem item)
    {
        // Normalize to the end so the visual order matches the service's append-to-end (a token can commit mid-strip).
        if (EnabledProviders.Count == 0 || !ReferenceEquals(EnabledProviders[^1], item))
        {
            if (EnabledProviders.Remove(item))
            {
                EnabledProviders.Add(item);
            }
        }

        _candidates.Remove(item);
        _preferences.SetEnabled(item.Id, true);
        FilterSuggestions(query: null);
        RaiseListDerivedChanged();
    }

    public void DisableProvider(LyricsProviderItem item)
    {
        if (!_candidates.Contains(item))
        {
            _candidates.Add(item);
        }

        _preferences.SetEnabled(item.Id, false);
        FilterSuggestions(query: null);
        RaiseListDerivedChanged();
    }

    public void CommitReorder(IReadOnlyList<LyricsProviderItem> orderedItems)
    {
        _preferences.Reorder(orderedItems.Select(i => i.Id).ToList());
        RaiseListDerivedChanged();
    }

    private void ResetProviders()
    {
        _preferences.ResetProviders();
        _initializing = true;
        try
        {
            RebuildFromSnapshot();
        }
        finally
        {
            _initializing = false;
        }
    }

    private void RebuildFromSnapshot()
    {
        _mutating = true;
        try
        {
            EnabledProviders.Clear();
            _candidates.Clear();

            foreach (var view in _preferences.GetProviders())
            {
                var item = new LyricsProviderItem(view.Id, view.DisplayName);
                if (view.Enabled)
                {
                    EnabledProviders.Add(item);
                }
                else
                {
                    _candidates.Add(item);
                }
            }
        }
        finally
        {
            _mutating = false;
        }

        FilterSuggestions(query: null);
        RaiseListDerivedChanged();
    }

    // EnabledProviders is mutated by TTB (add/remove) and by the reorder Drop. We only refresh derived UI state
    // here; persistence is committed by the explicit intents (Enable/Disable/CommitReorder), never here.
    private void OnEnabledProvidersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_initializing || _mutating)
        {
            return;
        }

        RaiseListDerivedChanged();
    }

    private void RaiseListDerivedChanged()
    {
        OnPropertyChanged(nameof(CanRemove));
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

- [ ] **Step 5: Add the placeholder string**

In `Hypersonic/Strings/en-us/Resources.resw`, add (mirror the existing `Settings_FontPicker.PlaceholderText`):

```xml
  <data name="Settings_LyricsProviderPicker.PlaceholderText" xml:space="preserve">
    <value>Type to add a provider…</value>
  </data>
```

- [ ] **Step 6: Replace the provider XAML block**

In `Hypersonic/Views/SettingsPage.xaml`, add the controls namespace at the top (alongside `xmlns:controls`; keep `xmlns:vm`):

```xml
    xmlns:hcontrols="using:Hypersonic.Controls"
```

Replace the provider `SettingsCard` body — the `<Grid ColumnDefinitions="*,Auto" …>` block containing `EnabledProvidersList`, the disabled `ItemsControl`, and `ResetLyricsProvidersButton` — with:

```xml
                <Grid ColumnDefinitions="*,Auto" ColumnSpacing="8">
                    <hcontrols:ReorderableTokenizingTextBox
                        Grid.Column="0"
                        x:Name="LyricsProviderBox"
                        x:Uid="Settings_LyricsProviderPicker"
                        ItemsSource="{x:Bind LyricsViewModel.EnabledProviders, Mode=OneWay}"
                        SuggestedItemsSource="{x:Bind LyricsViewModel.ProviderSuggestions, Mode=OneWay}"
                        TextMemberPath="DisplayName"
                        TextChanged="OnLyricsProviderTextChanged"
                        TokenItemAdding="OnLyricsProviderTokenAdding"
                        TokenItemAdded="OnLyricsProviderTokenAdded"
                        TokenItemRemoving="OnLyricsProviderTokenRemoving"
                        TokenItemRemoved="OnLyricsProviderTokenRemoved"
                        ItemsReordered="OnLyricsProviderItemsReordered">
                        <hcontrols:ReorderableTokenizingTextBox.QueryIcon>
                            <FontIconSource Glyph="&#xE721;" FontSize="12" />
                        </hcontrols:ReorderableTokenizingTextBox.QueryIcon>
                        <hcontrols:ReorderableTokenizingTextBox.TokenItemTemplate>
                            <DataTemplate x:DataType="vm:LyricsProviderItem">
                                <TextBlock Text="{x:Bind DisplayName}" VerticalAlignment="Center" />
                            </DataTemplate>
                        </hcontrols:ReorderableTokenizingTextBox.TokenItemTemplate>
                        <hcontrols:ReorderableTokenizingTextBox.SuggestedItemTemplate>
                            <DataTemplate x:DataType="vm:LyricsProviderItem">
                                <TextBlock Text="{x:Bind DisplayName}" />
                            </DataTemplate>
                        </hcontrols:ReorderableTokenizingTextBox.SuggestedItemTemplate>
                    </hcontrols:ReorderableTokenizingTextBox>

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
```

> The now-unused `Settings_LyricsDisabledHeader` resw entry may be left in place (harmless) or removed.

- [ ] **Step 7: Wire the code-behind**

In `Hypersonic/Views/SettingsPage.xaml.cs`, add the handlers (mirroring the font pickers where applicable). Suggestions are VM-owned, so the page holds no provider suggestions collection:

```csharp
    private void OnLyricsProviderTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        LyricsViewModel.FilterSuggestions(sender.Text);
    }

    private void OnLyricsProviderTokenAdding(TokenizingTextBox sender, TokenItemAddingEventArgs args)
    {
        // Resolve the typed text to a stable provider item; reject unknown/already-enabled text.
        var item = LyricsViewModel.ResolveCandidate(args.TokenText);
        if (item is null)
        {
            args.Cancel = true;
        }
        else
        {
            args.Item = item; // TTB adds this object instead of the raw string
        }
    }

    private void OnLyricsProviderTokenAdded(TokenizingTextBox sender, object data)
    {
        if (data is LyricsProviderItem item)
        {
            LyricsViewModel.EnableProvider(item);
        }
    }

    private void OnLyricsProviderTokenRemoving(TokenizingTextBox sender, TokenItemRemovingEventArgs args)
    {
        if (!LyricsViewModel.CanRemove)
        {
            args.Cancel = true; // never remove the sole remaining enabled provider
        }
    }

    private void OnLyricsProviderTokenRemoved(TokenizingTextBox sender, object data)
    {
        if (data is LyricsProviderItem item)
        {
            LyricsViewModel.DisableProvider(item);
        }
    }

    private void OnLyricsProviderItemsReordered(Controls.ReorderableTokenizingTextBox sender, IReadOnlyList<object> newOrder)
    {
        LyricsViewModel.CommitReorder(newOrder.Cast<LyricsProviderItem>().ToList());
    }
```

Add `using System.Linq;` if not present (for `Cast`). The constructor's existing `_ = LyricsViewModel.InitializeAsync();` and `ApplyResetButtonAccessibility(ResetLyricsProvidersButton);` are unchanged (the VM seeds `ProviderSuggestions` during initialization).

- [ ] **Step 8: Build the app**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: build succeeds — no references remain to the removed `IsEnabled`/`CanToggle`, `DisabledProviders`, or `ShowDisabledDivider`.

- [ ] **Step 9: Run the full app test suite**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS.

- [ ] **Step 10: Manual verification in the running app (F5 / deploy MSIX)**

Open Settings → Lyrics and verify:
- Fresh profile shows exactly **QQ Music, Subsonic, LrcLib** as chips; AMLL / NetEase / Kugou appear in the dropdown on focus/typing.
- **Add:** pick from the dropdown OR type an exact name → a chip appears **at the end**; it leaves the dropdown.
- **Reject:** gibberish, wrong case, or an already-enabled name does not create a chip.
- **Remove:** the chip `×` removes it and it returns to the dropdown.
- **Last-chip block:** down to one chip, its `×` no longer removes it.
- **Reorder:** drag a chip; order changes, the trailing input stays last, and the dropped chip never lands after the input. Also drag when chips wrap to a second row (resize the window narrow) — the drop lands in the intended reading-order slot.
- **Reset:** ⟳ restores QQ → Subsonic → LrcLib, the dropdown immediately shows AMLL/NetEase/Kugou again, and the button greys out at the default.
- **Persistence:** relaunch — chip set/order and the two budget sliders are unchanged.

- [ ] **Step 11: Commit**

```bash
git add Hypersonic/ViewModels/LyricsProviderItem.cs Hypersonic/ViewModels/LyricsSettingsViewModel.cs Hypersonic/Views/SettingsPage.xaml Hypersonic/Views/SettingsPage.xaml.cs Hypersonic/Strings/en-us/Resources.resw Hypersonic.Tests/ViewModels/LyricsSettingsViewModelTests.cs
git commit -m "feat(lyrics): tokenizing-box provider settings (object chips + autosuggest + drag reorder)"
```

---

## Self-Review

**Spec coverage:**
- Tokenizing chip box replacing the stacked list → Task 4. ✓
- Extend TTB, no fork, no `CanReorderItems`, Remove+Insert on bound collection → Task 3. ✓
- Object identity (`LyricsProviderItem`, `TextMemberPath`, `args.Item`) → Global Constraints + Task 4. ✓ (review #1)
- Row-aware drop resolution + wrapped-row/single-chip tests → Task 1. ✓ (review #2)
- Canonical order preserved; priority via a separate ordered list → Task 2. ✓ (review #3)
- Fake reset preserves DisplayName → Task 2 Step 8. ✓ (review #4)
- New chip normalized to end → Task 4 `EnableProvider` + test. ✓ (review #5)
- Suggestions refreshed after reset (VM-owned) → Task 4. ✓ (review #6)
- Enable/disable committed in post-gesture events; last-chip block in `TokenItemRemoving` → Task 4. ✓ (deliberately kept per review #7 discussion)
- Companion default (QQ→Subsonic→LrcLib), new providers append disabled, existing installs preserved, `CanReset` vs default set → Task 2. ✓
- Reconciler unknown/deprecated drop unchanged (step 1 preserved) → Task 2. ✓

**Review round 2 (addressed):**
- `BeforeLoad_ReportsAllEnabledDefaults` updated to the default set + real `Create(…, AllProviders())` helper (P1) → Task 2 Step 9. ✓
- Explicit "old all-enabled install preserved" reconciler test (P2) → Task 2 Step 1. ✓
- Reset test restores the budget-preservation assertion (P2) → Task 2 Step 9. ✓
- VM budget tests retained (`ResetAutoLoad_RestoresDefault` + `prefs.AutoLoadBudget` persistence assertion) (P2) → Task 4 Step 1. ✓
- `_dragged` cleared via `DragItemsCompleted` on every completion path (P2) → Task 3. ✓

**Placeholder scan:** none — every code step shows complete code; manual steps enumerate concrete checks. ✓

**Type consistency:** `EnabledProviders`/`ProviderSuggestions` (`ObservableCollection<LyricsProviderItem>`), `ResolveCandidate`/`FilterSuggestions`/`EnableProvider`/`DisableProvider`/`CommitReorder`, `CanRemove`, `CanResetProviders`, `DefaultEnabledProviderIds`, `ReorderDropIndex.Resolve(x,y,bounds)`, `ItemsReordered : IReadOnlyList<object>` are consistent across Tasks 1–4 and the tests. ✓

**Manual-verification linchpins (carried into execution):** (a) programmatic Remove+Insert reorders chips while keeping the editor last (Task 4 Step 10); (b) `args.Item` substitution adds the object for free-typed text; (c) wrapped-row drop resolution. If Remove+Insert ever fails to re-project, fall back to a collection `Reset` on reorder (design Risks).

---

## Execution Handoff

Plan revised and saved to `docs/implementation/ui/2026-07-12-lyrics-provider-settings-tokenizing-box.md`.
