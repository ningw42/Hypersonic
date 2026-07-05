# Lyrics Provider Settings — Design

A Settings surface that lets the user **choose which lyrics sources participate, in what priority order, and how long the two fetch waits run** — and persists all of it. Builds directly on the shipped lyrics stack: the `ILyricsProvider` fan-out and quality-tiered `LyricsFallbackPolicy` (`docs/design/service/2026-06-09-lyrics-provider-service.md`), the concurrent budgeted orchestration in `LyricsService`, and the manual-refresh path in `NowPlayingViewModel` (`docs/design/ui/2026-06-21-lyrics-now-playing-integration.md`).

No source code — see the forthcoming implementation plan for that.

---

## Scope of this slice

Four user-visible capabilities, one new owning service, and a rewire of two existing consumers:

- ✅ **Enable/disable each provider** — an unchecked source is not fetched during fan-out.
- ✅ **Reorder providers by drag-and-drop** — order is the fan-out precedence, i.e. the *tiebreak* the existing policy already uses.
- ✅ **Two adjustable wait windows** — the automatic track-change budget and the manual-refresh budget, exposed as sliders in seconds.
- ✅ **Persistence** across packaged (MSIX) and unpackaged runs, via `ILocalSettingsService`.
- ✅ **A new `ILyricsPreferences` service** that owns all of the above and is consulted live by the orchestrator, so changes take effect on the next fetch with no restart.

**Explicitly not** in scope: changing *how the winner is chosen* (§Pivotal decisions), per-provider configuration (API keys, regions, per-source timeouts), or cache invalidation when a provider is disabled.

---

## The one product decision that shaped everything: priority is a tiebreak, not an override

Today the winner is chosen by **sync quality** — `Word > Line > Plain` — with provider registration order breaking ties *among equal tiers* only (`LyricsFallbackPolicy.Decide`, strict `>` keeps the earliest). A word-synced result always beats a line-synced one regardless of list position.

The user chose to **keep that rule intact**. So "drag a provider higher" means: *when two sources return equally-synced lyrics, the higher one wins.* It does **not** mean a plain-text source can leapfrog an available synced source. The consequence is architecturally clean:

- `LyricsFallbackPolicy` is **untouched**.
- Ordering the fan-out array by the user's precedence is sufficient — the policy's existing "results are ordered by provider precedence, earliest wins a tie" contract does the rest.
- Enabling/disabling is a pure **filter** on which providers enter the fan-out.

This is the cheapest possible design that satisfies the feature, and it preserves the quality guarantee the karaoke renderer depends on. The two rejected alternatives (strict top-source-wins; synced-first-then-strict-order) would each have required reworking the winner-selection policy and were declined.

---

## What we're building, and where it lives

The lyrics stack already splits pure logic (`Hypersonic.Core`) from app plumbing (`Hypersonic`). This slice adds one service and one Settings surface, both in `Hypersonic`, plus one pure helper that can be exercised in `Hypersonic.Core.Tests`.

**`Hypersonic` — the new owner and the consumers it feeds:**

- **`ILyricsPreferences` / `LyricsPreferences` (singleton)** — the single source of truth for the enabled set, the order, and both budgets. The lyrics analog of `IScrobblingService`: one service that the Settings ViewModel binds to, the runtime reads from, and that persists its own state. Lives beside `LyricsService`.
- **`LyricsService` (rewired)** — orders + filters its injected provider set through the preferences snapshot at fetch time, and reads the auto budget from it.
- **`NowPlayingViewModel` (rewired)** — reads the manual-refresh budget from the preferences service.
- **`SettingsViewModel` / `SettingsPage.xaml` (extended)** — a new "Lyrics" section binding to the preferences service.

**`Hypersonic.Core` — the reconcile/clamp logic and the persisted state record:**

- A **pure reconcile function** turning `(persisted state, registered provider ids, default budgets as plain ints)` into a validated effective snapshot, plus the small persisted record it reads. Kept free of WinUI/persistence so its rules (§Reconcile) are unit-testable in `Hypersonic.Core.Tests` in isolation. It takes the default budgets as primitives, so it needs no reference to `LyricsOrchestrationOptions` (which lives in the app project) — the service reads that type and passes the two ints in.

**Unchanged:** `LyricsSourceCatalog` keeps its single job — mapping a provider id to its display name for the Now Playing source badge. It is *not* grown into a mutable registry; preference state is the new service's concern, and orchestration budgets have no place in a "source catalog."

**DI (`App.xaml.cs`):** register `ILyricsPreferences` as a singleton before `LyricsService`. `LyricsOrchestrationOptions` stays registered as the **default-seed** for the two budgets (see §Budgets) — only the preferences service reads it now; `LyricsService` and `NowPlayingViewModel` drop their direct `IOptions<LyricsOrchestrationOptions>` dependency.

---

## Pivotal decisions

### The preferences service holds an atomically-swapped immutable snapshot

The fan-out runs off the UI thread; Settings mutates preferences on the UI thread. Rather than lock every read, the service holds a single immutable snapshot record (the ordered `(Id, Enabled)` list plus the two budgets) behind a `volatile` reference. A mutation builds a new snapshot and swaps the reference; readers take the current reference and use it consistently for the duration of one fetch. Lock-light, race-free, and cheap on the hot path.

### Load is async and idempotent; the first fetch never races it

`ILocalSettingsService` is async, and a track can start playing moments after launch. The service exposes an idempotent `EnsureLoadedAsync()` (a cached `Task`, the `InitializeFontSettingsAsync` idiom already in `SettingsViewModel`). It is awaited:

- at the top of `LyricsService.GetLyricsAsync` (already async — a trivial await of an already-completed task after first load),
- by the manual-refresh command before it reads the manual budget,
- by the Settings page initializer before it projects the provider list.

Until loaded, the service reports built-in defaults, so nothing ever reads uninitialized state.

### Reconcile makes the persisted list robust to a changing provider set {#reconcile}

The registered provider set can grow or shrink across app versions. On load, the pure reconcile function:

1. Keeps persisted entries whose `Id` is **still registered**, in their saved order.
2. **Appends** any registered provider absent from the persisted list at the end, **enabled** (a newly-shipped source is on by default, lowest priority).
3. **Drops** persisted ids that no longer resolve to a registered provider.
4. Enforces the **at-least-one-enabled invariant** — if reconciliation leaves zero enabled, it enables the first entry.
5. **Clamps** each budget to its allowed range; a missing or out-of-range value falls back to the default.

First run (no persisted state): every provider enabled, registration order, default budgets.

### At least one provider stays enabled — enforced at the edit, not just at load

The user chose to forbid an all-disabled state. This is enforced in two places: reconcile guarantees a loaded snapshot always has ≥1 enabled, and the Settings UI prevents unchecking the final enabled provider by locking its checkbox (§Settings UI). The service's `SetEnabled` also refuses a mutation that would drop the count to zero, so the invariant holds regardless of caller.

### Budgets keep a single source of default numbers {#budgets}

`LyricsOrchestrationOptions` (`DefaultFanOutBudgetMs = 700`, `ManualRefreshBudgetMs = 5000`) is **retained as the default seed** rather than duplicating those magic numbers in the new service. The preferences service reads it once for its defaults, then persisted user overrides win. Live values always come from the snapshot. The two consumers stop reading `IOptions` directly and ask the preferences service instead — so there is exactly one runtime authority for "what budget applies right now."

The sliders expose **seconds**; the service stores and serves **milliseconds** (the unit the orchestrator and `Task.Delay` already use). Proposed ranges — auto-load **0.3–3.0 s** (step 0.1 s, default 0.7 s); manual-refresh **1–15 s** (step 0.5 s, default 5 s) — are clamp bounds in the reconcile step, not just UI limits.

### Disabling a provider affects future fetches only

A disabled provider is skipped in fan-out, but **already-cached lyrics keep serving** — the cache is keyed by song and is source-agnostic (`LyricsService` reads the cache before any fan-out). A user who disables a source and wants its cached result gone hits the existing **manual refresh**, which re-runs the fan-out under the new enabled set and overwrites the entry. No cache sweep on toggle; no new invalidation surface.

---

## Contracts (conceptual)

- **`ILyricsPreferences`**
  - `Task EnsureLoadedAsync(ct)` — idempotent load + reconcile.
  - Reads (valid after load, synchronous): the **ordered enabled provider ids** (what `LyricsService` fans out over), `AutoLoadBudget` / `ManualRefreshBudget` (`TimeSpan`), and a **display list** of `(Id, DisplayName, Enabled)` for the UI.
  - Mutations: `SetEnabled(id, enabled)`, `Reorder(orderedIds)`, `SetAutoLoadBudget`, `SetManualRefreshBudget` — each rebuilds and swaps the snapshot, then persists. Toggle/reorder persist immediately; slider writes are debounced (the existing `QueueFontSettingSave` serialized-write idiom).
- **Persisted state object** — a small record `{ Providers: [{ Id, Enabled }], AutoLoadBudgetMs, ManualRefreshBudgetMs }` (in `Hypersonic.Core`), stored under a single `ILocalSettingsService` key `"LyricsPreferences"`. One key = one atomic write, and the reconcile function is its only reader/validator. Absent key → reconcile produces the first-run defaults; no migration.
- **`LyricsService`** — unchanged public contract. Internally, `RunFanOutAsync` builds its working array by projecting the injected `ILyricsProvider` instances through the snapshot's ordered-enabled id list (preserving "ordered by precedence" for the tiebreak); the resolved auto budget comes from the preferences service.
- **`NowPlayingViewModel`** — unchanged public contract; the refresh command's budget now comes from `ILyricsPreferences.ManualRefreshBudget`.

---

## Settings UI — a new "Lyrics" section

A `SettingsExpander` matching the Scrobbling/Fonts sections, containing three cards:

- **Provider list** — a `SettingsCard` (`ContentAlignment="Vertical"`) wrapping a reorderable `ListView` (`CanReorderItems` / `AllowDrop` / `ReorderMode`) bound to `ObservableCollection<LyricsProviderItem>`. Each row: a `CheckBox` (two-way to `IsEnabled`), the provider `DisplayName`, and a drag-handle glyph. A drag reorders the bound collection and raises `CollectionChanged (Move)` → the VM calls `Reorder` (the font-list `CollectionChanged`-driven save pattern, already in this VM).
- **Two budget sliders** — each a `SettingsCard` with a `Slider` in seconds and a live value readout in the card `Description` (the scrobble-percentage-slider idiom).
- **Reset to defaults** — a button mirroring the existing Reset buttons (re-enables all, restores registration order and default budgets).

`LyricsProviderItem : ObservableObject { Id; DisplayName; IsEnabled; CanDisable }`. The **at-least-one-enabled** rule is expressed by binding each `CheckBox.IsEnabled` to `item.CanDisable`; the VM recomputes `CanDisable = enabledCount > 1` for every item whenever the enabled count changes, so the sole remaining enabled provider's checkbox locks. The VM projects the preferences display list into items in an async initializer (awaiting `EnsureLoadedAsync`), and localized strings are added to `Strings/en-us/Resources.resw`.

---

## Data flow

**Startup / connect:** `ILyricsPreferences.EnsureLoadedAsync` runs (lazily on first use; the first track-change fetch or Settings open triggers it). Reconcile produces the effective snapshot.

**Track change:** `LyricsService.GetLyricsAsync` → cache check (unchanged) → on miss, `RunFanOutAsync` reads the current snapshot, fans out over **only the enabled providers in user order**, under the **auto budget** from the snapshot → `LyricsFallbackPolicy.Decide` (unchanged) picks the winner.

**Manual refresh:** `NowPlayingViewModel` refresh command reads the **manual budget** from the snapshot and calls `GetLyricsAsync(..., forceRefresh: true)` (unchanged path) — re-deciding under the current enabled set.

**Settings edit:** a toggle / drag / slider calls the corresponding mutation → snapshot swap → persist. The next fetch reads the new snapshot; nothing else is signalled and no restart is needed.

---

## Testing strategy

- **Reconcile/clamp (pure, `Hypersonic.Core.Tests`)** — unknown-id drop, new-id append-enabled-lowest-priority, at-least-one-enforcement when a persisted set is emptied, budget clamping to range, first-run defaults, and a persisted→reconcile→persisted round-trip.
- **`LyricsService` (`Hypersonic.Tests`)** — a disabled provider is never called; fan-out order follows the snapshot; the auto budget is sourced from the preferences service (verified via `FakeTimeProvider` as existing service tests do).
- **`NowPlayingViewModel`** — refresh uses the manual budget from the preferences service (extends the existing `Refresh_ForceRefreshesWithManualBudget`).
- **`SettingsViewModel`** — toggle and reorder persist through the service; the last-enabled `CanDisable` lock; slider debounce writes the clamped value.
- **`LyricsPreferences` service** — `EnsureLoadedAsync` idempotency; snapshot swap visibility; mutation-refuses-zero-enabled invariant.

---

## Deferred / out of scope

- **Winner-selection changes** — the `Word > Line > Plain` policy is deliberately preserved; priority remains a tiebreak.
- **Per-provider configuration** — API keys, region/market selection, per-source timeouts.
- **Cache invalidation on disable** — cached lyrics keep serving until the user refreshes or the entry expires (§Pivotal decisions).
- **Growing `LyricsSourceCatalog`** — it stays a display-name lookup.

---

## Risks / open questions

- **Load-race hardening** — awaiting `EnsureLoadedAsync` inside `GetLyricsAsync` adds one await to the lyrics hot path; after first load it resolves synchronously, but the await must be placed before the snapshot is read, not after.
- **Reorder + checkbox in one `ListView`** — a checkbox inside a drag-reorderable row can swallow the pointer intended for the drag handle. The drag handle glyph should be the reorder affordance; verify checkbox taps and drags don't interfere (a known WinUI `ListView` reorder nuance).
- **Provider `DisplayName` source** — the UI shows `ILyricsProvider.DisplayName`; the preferences service enumerates the injected providers to build the display list, so a provider missing from DI simply drops out of the list (consistent with reconcile step 3).

---

## References

- Provider fan-out + policy this builds on: `docs/design/service/2026-06-09-lyrics-provider-service.md`; `Hypersonic/Services/LyricsService.cs`, `Hypersonic/Services/Lyrics/LyricsFallbackPolicy.cs`.
- Manual-refresh path being rewired: `docs/design/ui/2026-06-21-lyrics-now-playing-integration.md`; `Hypersonic/ViewModels/NowPlayingViewModel.cs`.
- Budgets seed: `Hypersonic/Models/LyricsOrchestrationOptions.cs`.
- Service-with-persistence pattern mirrored: `IScrobblingService`; the settings-binding + `CollectionChanged`-save idioms in `Hypersonic/ViewModels/SettingsViewModel.cs`.
- Persistence contract: `ILocalSettingsService` (packaged + unpackaged); Settings idioms in `Hypersonic/Views/SettingsPage.xaml`.
- Display-name lookup left unchanged: `Hypersonic/Services/LyricsSourceCatalog.cs`.
