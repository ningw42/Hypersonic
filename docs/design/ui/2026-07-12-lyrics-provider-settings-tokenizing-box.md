# Lyrics Provider Settings — tokenizing-box redesign

A redesign of the provider list in **Settings → Lyrics**. The section shipped as a stacked two-group list (an "Enabled" reorderable `ListView` of checkbox rows over a "Disabled" group). This replaces that block with a **single tokenizing chip box**: the enabled providers are chips in priority order, an inline trailing autosuggest input adds more, the chip `×` removes one, and **drag reorders** the chips. It reuses the `TokenizingTextBox` already driving the two font pickers in this same section — extended with one new capability, reorder.

This builds on the shipped provider-settings slice (`docs/design/service/2026-07-05-lyrics-provider-settings.md`). The chip UI is **presentation-only** — it binds to the service's existing mutation surface (`SetEnabled` / `Reorder` / `Reset`), which is unchanged. It ships alongside **one small, isolated service change**: the default enabled set that first-run and reset seed (§Companion change). Persistence, live application, and the reconcile of *existing* installs are otherwise untouched.

No source code — see the forthcoming implementation plan for that.

---

## Scope

Confined to the settings surface — one new control, a handful of files, and resource strings:

- **`Hypersonic/Controls/ReorderableTokenizingTextBox.cs`** (new) — a thin `TokenizingTextBox` subclass that adds drag-to-reorder over the chips.
- **A small pure drop-index helper** (new) — the insertion-index arithmetic, extracted so it is unit-testable (see §Testing).
- **`SettingsPage.xaml`** — the Providers `SettingsCard` body is rebuilt as the reorderable tokenizing box + reset icon.
- **`SettingsPage.xaml.cs`** — suggestion filtering + token add/remove validation, following the existing font-picker handlers.
- **`LyricsSettingsViewModel`** — the checkbox-driven enable/disable plumbing is replaced by explicit intent methods for add / remove / reorder.
- **`LyricsProviderItem`** — loses its checkbox role (`IsEnabled` two-way, `CanToggle`); chip-strip membership *is* the enabled state.
- **`Strings/en-us/Resources.resw`** — box placeholder, reset tooltip.

The **companion default-set change** (§Companion change) additionally touches, in the service layer:

- **`LyricsPreferencesReconciler`** (`Hypersonic.Core`) — first-run/append branch seeds only the default-enabled set.
- **`LyricsPreferences`** — a pinned default-enabled constant; `ResetProviders` seeds that set; expose it for the reset-availability check.
- **`ILyricsPreferences`** — expose the default-enabled ids (in priority order).
- **`Hypersonic.Core.Tests` / `Hypersonic.Tests`** — reconciler + service default coverage (revising the shipped `FirstRun_AllEnabled…` / `ResetProviders_RestoresAllEnabled…` tests).

**Explicitly unchanged:** the `ILyricsPreferences` **mutation surface** (`SetEnabled` / `Reorder` / `SetAutoLoadBudget` / `SetManualRefreshBudget`), the persisted state shape and its storage key, the budget clamp logic and the two budget sliders + their inline resets, the reconcile of *existing* persisted installs, and every downstream consumer (`LyricsService`, `NowPlayingViewModel`, `LyricsFallbackPolicy`).

---

## The decision that keeps the chip UI presentation-only: the service already expresses everything

The shipped `ILyricsPreferences` contract is sufficient for the tokenizing box with **no additions**:

- **Enable** (commit a chip from the autosuggest) is `SetEnabled(id, true)` — which appends the provider to the end of the enabled run and already enforces the at-least-one-enabled invariant. A newly-added chip lands at the **end** (lowest priority); the user drags it up.
- **Disable** (the chip `×`) is `SetEnabled(id, false)` (refuses to drop the count to zero).
- **Reprioritize** (drag a chip) is `Reorder(enabledIdsInNewOrder)`, exactly as today.
- **Reset** is `ResetProviders()`, exactly as today.

Every mutation is a single, direct service call from the intent that caused it — no compose-two-calls transient, since an add appends at the end (matching `SetEnabled`'s own append) and a drag is a self-contained `Reorder`. The in-memory snapshot swaps synchronously on the UI thread; the next fetch reads the final state.

---

## Companion change: the default enabled set

Separately from the chip UI, first-run seeding and **reset** change from *all providers enabled* to an opinionated default: **QQ → Subsonic → LrcLib enabled (in that priority order), with AMLL / NetEase / Kugou shipped disabled**. This is the one service-side edit in this effort, and it is isolated from the UI — the box renders whatever is enabled either way.

- **Where it lives.** A pinned **default-enabled set** (distinct from the canonical membership order) drives two spots: the reconciler's first-run/append branch (which today hardcodes every appended id `Enabled = true`) and `LyricsPreferences.ResetProviders` (which today builds all-enabled). Nothing else in the service moves.
- **First run & reset** → enabled = {QQ, Subsonic, LrcLib} in priority order; everything else disabled. The enabled-first ordering invariant and the at-least-one-enabled guard are preserved.
- **New providers ship disabled.** Because the reconciler's "append a registered id absent from the persisted list" step now enables an id only if it's in the default set, a genuinely new future source appends **disabled** rather than silently joining the fan-out.
- **Existing installs keep their choices.** Reconcile still keeps persisted rows in their saved order and enabled state (step 1 unchanged); the new default only affects a fresh install or an explicit reset. An install already carrying the old all-enabled state stays that way until the user resets — force-migrating existing state is deliberately not done.
- **Reset availability.** `CanResetProviders` compares the enabled chips against the default-enabled set (not "any provider disabled"), so the reset icon lights only when the current enabled set/order differs from the default. The service exposes the default-enabled ids for this check.
- **Priority-order rationale.** QQ first (frequent word-synced results); among equal-tier ties, the user's own Subsonic server beats community LrcLib. Order is only a tiebreak under the unchanged `Word > Line > Plain` policy, so this is low-stakes and fully adjustable in the box.

---

## Approach: extend `TokenizingTextBox`, don't rebuild or fork

`TokenizingTextBox` derives from `ListViewBase`, so it already carries the drag/drop plumbing (`CanDragItems`, `AllowDrop`, `DragItemsStarting`, `DragOver`, `Drop`). It has **no built-in reorder**, but two facts from its source make adding one cheap and non-invasive:

1. Its ItemsPanel is CommunityToolkit's **`WrapPanel` with `StretchChild="Last"`** — the trailing text-entry cell stretches to fill the last row. That is exactly the inline-trailing-input look this redesign wants, and it is non-virtualizing, so every chip container is realized and hit-testable.
2. Its tokens live in an internal `InterspersedObservableCollection` that wraps the bound `ItemsSource` and splices in the trailing editor. That collection **reflects `Add` / `Remove` / `Reset` performed on the bound source** (it does **not** handle `Move`).

Together these dictate the mechanism:

- **Reorder by mutating the *bound* collection, never TTB's internals.** A drag reorder is applied as a **`Remove` + `Insert` pair** on the bound `ObservableCollection` (a `Move` would be ignored by the interspersed collection; a Remove+Insert is faithfully re-projected, with the editor kept last). No private field is touched; no fork is needed.
- **Do *not* set `CanReorderItems`.** That would hand the interspersed collection (editor included) to the framework's automatic reorder. Instead the subclass owns the drop explicitly — an explicit `Drop` is the sole reorder mutator, scoped inside one control.
- **A subclass, not code-behind.** The drag logic is bulkier than the font pickers' one-line `TokenItemAdding` guard and benefits from being self-contained and independently testable. (Wiring it in `SettingsPage.xaml.cs` against a plain TTB was considered — it would match the font-picker precedent — but the encapsulated control keeps the page thin and the drop-index math isolated.)
- **A fork was rejected.** Vendoring the toolkit component's source would reuse its behavior too, but it is far more code to carry and drift; the subclass reuses 100% of TTB by inheritance and adds only what's new.

---

## Layout

The Providers `SettingsCard` (`ContentAlignment="Vertical"`) holds the reorderable tokenizing box with the provider reset icon top-right.

```
┌─ Lyrics providers ──────────────────────────────  [ ⟳ ] ┐
│ ┌──────────────────────────────────────────────────────┐ │
│ │ [QQ ×] [Subsonic ×] [LrcLib ×]  | type to add…        │ │
│ └──────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────┘
   default enabled set shown; AMLL / NetEase / Kugou live in the dropdown
   chips wrap · drag to reprioritize · × removes (disables) · type to add
```

- **Chips = enabled providers, in priority order** (left→right, wrapping). Each chip renders the provider `DisplayName` and carries TTB's built-in `×` remove button.
- **Inline trailing autosuggest input** — the stretched last cell. Typing filters the remaining (disabled) providers by name; committing a match adds a chip.
- **Reset icon** — `SubtleButtonStyle`, 32 px, glyph `E777`, top-right of the card → resets to the default enabled set (QQ → Subsonic → LrcLib enabled, the rest disabled — §Companion change) (`ResetProvidersCommand`, `CanResetProviders`-gated). Same control as today; its target is the new default.
- **Disabled providers are not shown as chips** — they exist only as autosuggest candidates. This is inherent to the token model: the box shows what's *on*; the dropdown offers what's *off*. There is no separate "Available" list.
- **Degenerate width** — the chip box wraps naturally and TTB already trims long tokens; no special narrow-window handling beyond what TTB and the card provide.

---

## Interactions

Every input routes through **one set of VM intent methods**, so the paths never diverge.

### Add (enable)
Type into the trailing input; the dropdown suggests remaining providers matched by name. Committing a suggestion (or typed text that resolves to a remaining candidate) adds a chip **at the end** of the enabled run and calls `SetEnabled(id, true)`. Text that matches no remaining candidate is **rejected** (`TokenItemAdding` cancels, mirroring the font pickers' `OnFontTokenAdding` rejecting non-fonts) — so every chip is always a real, not-already-enabled provider.

### Remove (disable)
The chip `×` raises TTB's cancelable `TokenItemRemoving`. It removes the chip and calls `SetEnabled(id, false)`, returning the provider to the autosuggest candidate pool.

### The last-enabled block
When the box holds exactly one chip, `TokenItemRemoving` is **cancelled** — the sole enabled provider cannot be removed. The service's `SetEnabled` refusing a zero-enabled mutation is the backstop.

### Reorder (reprioritize)
Drag a chip; on drop, the subclass resolves the insertion index and reorders the bound collection, then calls `Reorder(enabledIdsInNewOrder)`. Dragging is the **only** way to set priority (a newly-added chip starts last), matching how re-checking behaved in the shipped UI. Reorder is **pointer/touch only** — TTB has no keyboard reorder, and none is added (§Deferred).

---

## The control — `ReorderableTokenizingTextBox`

A `TokenizingTextBox` subclass whose entire job is the drag reorder. Everything else — chip template, autosuggest dropdown, `×` button, typing/commit/focus, the stretched editor — is inherited unchanged.

- **Enables drag:** `CanDragItems` + `AllowDrop`; leaves `CanReorderItems` off.
- **`DragItemsStarting`** — cancels the drag unless the dragged data item is one of the bound chip items. The internal editor placeholder is not a chip item, so a drag can never originate from (or target) the input cell.
- **`DragOver`** — `AcceptedOperation = Move`.
- **`Drop`** — feeds the pointer position and the realized chip-container bounds to the **pure drop-index helper**, which returns an insertion index in `[0, chipCount]` (left/right of each chip's midpoint; **clamped so a chip can never land after the trailing editor**). The control then applies the move to the bound collection as a **Remove + Insert**, marks the event handled (so no framework reorder also fires), and surfaces the new order to the VM (event or callback) for the `Reorder` commit.

The subclass holds no preferences knowledge — it manipulates its bound items and reports the resulting order; the VM owns the service call.

---

## ViewModel & wiring

`LyricsSettingsViewModel` keeps its enabled `ObservableCollection<LyricsProviderItem>` (bound to the box's `ItemsSource`) and a candidate source feeding the suggestions, plus its `_initializing` / `_mutating` guards, and exposes explicit intents:

- **Enable** — on a validated token add: commit `SetEnabled(id, true)` and drop the provider from the candidate pool. (TTB appends the chip itself, so order = end.)
- **Disable** — on `TokenItemRemoving` (when not the last chip): commit `SetEnabled(id, false)` and return the provider to the candidate pool.
- **Reorder** — on the control's drop: `Reorder(enabledIds)` under the `_mutating` guard.
- **Reset** — `ResetProviders()` (now seeding the default-enabled set — §Companion change) then rebuild both the chip collection and the candidate pool. `CanResetProviders` compares the enabled chips against the default-enabled set.

**Persistence is driven by the intents, not by `CollectionChanged`.** Today's `OnEnabledCollectionChanged → Reorder` handler is removed: because TTB mutates the bound collection itself on add/remove *and* the drop applies a Remove+Insert, a `CollectionChanged`-based persist can't cleanly tell "enable" from "disable" from "reorder." Routing each through its own intent removes that ambiguity; the `_mutating` guard prevents re-entrancy while an intent edits the collection.

**Suggestions** are the disabled providers filtered by the typed text — the exact `UiFontSuggestions` idiom already in `SettingsPage.xaml.cs` (`SuggestedItemsSource` + a `TextChanged` filter), rendering `DisplayName`.

**`LyricsProviderItem`** sheds `IsEnabled` (two-way) and `CanToggle`; `Id` / `DisplayName` remain. Enabled-ness is now expressed by which collection the item lives in.

---

## Data flow

**Settings open:** `LyricsSettingsViewModel.InitializeAsync` awaits `EnsureLoadedAsync`, then projects the preferences display list into the enabled chip collection (priority order) and the disabled candidate pool — as today, just into a different shape.

**A change (add / remove / drag):** the input routes to an intent method → the collection edit + the explicit `SetEnabled` / `Reorder` call → snapshot swap → serialized persist. The next fetch reads the new snapshot; nothing else is signalled and no restart is needed — identical to the shipped behavior, only the trigger differs.

---

## Testing

- **VM intent tests** (`Hypersonic.Tests`, against the existing `FakeLyricsPreferences`):
  - Add enables at the end (collection order + `SetEnabled(true)`), and a non-candidate is rejected.
  - Remove disables and returns to candidates; **no-ops / is refused on the last chip**.
  - Reorder persists the new order (`Reorder` with the expected id sequence).
  - Reset restores the default-enabled set (QQ → Subsonic → LrcLib) + candidate pool, with its `CanReset` gate lit only when the current enabled set/order differs from that default.
  - Budget-slider tests carry over unchanged.
- **Drop-index math is a pure helper, unit-tested directly** — drop left/right of a chip midpoint, drop past the last chip (clamped before the editor), and degenerate single-chip / empty cases. Only the WinUI hit-test plumbing that feeds it stays manual.
- **Drag-and-drop event wiring is UI behavior → manual verification** (matching how the lyrics-canvas drag work was verified).
- **Default-set coverage (`Hypersonic.Core.Tests` + `Hypersonic.Tests`)** — first run seeds {QQ, Subsonic, LrcLib} enabled (rest disabled); reset restores that; a hypothetical new registered id appends **disabled**; an existing persisted (all-enabled) state is preserved through reconcile. This revises the shipped `FirstRun_AllEnabled…` / `ResetProviders_RestoresAllEnabled…` tests; the budget clamp/reconcile tests otherwise carry over.

---

## Risks / verify in the running app

- **Approach linchpin:** confirm a **Remove + Insert on the bound collection** visually reorders the chips *and keeps the trailing editor last*. The toolkit source shows `InterspersedObservableCollection` handles Add/Remove/Reset, so this should hold — it is manual-verification item #1. Fallback if it misbehaves: re-project via a `Reset` on reorder, or vendor the small TTB source.
- `DragItemsStarting` genuinely blocks a drag that starts on the trailing input cell; a chip can never be dropped after it.
- The `×` on the sole remaining chip does not remove it (`TokenItemRemoving` cancels), and the service backstop holds.
- Autosuggest matches remaining providers by name, rejects unknown/already-enabled text, and a committed chip lands last.
- Choices persist across a relaunch; the two budget sliders are untouched.

**Manual verification checklist:** add via the dropdown, remove via `×`, the last-chip block, drag-reorder in both directions (including to the last position), reset to defaults, sliders untouched, and choices persisting across a relaunch.

---

## Deferred / out of scope

- **Enable-at-a-chosen-priority by typing between chips** — an add always appends at the **end** (matching the service's `SetEnabled(true)` append, which keeps enable a single call with the visual and snapshot in agreement); **drag** is the way to prioritize. If TTB's caret ever lands a committed chip mid-strip, the enable intent normalizes by committing the collection order (the same `Reorder` a drag uses), so the two never drift.
- **Keyboard reorder** — TTB has none today and none is added; drag-reorder is pointer/touch only. An accessible reorder path (e.g. a context-menu Move up/down) can be added later if wanted.
- **Showing disabled providers outside the dropdown** — no "Available" list; disabled sources are rediscovered by typing.
- Everything the shipped slice deferred (winner-selection changes, per-provider configuration, cache invalidation on disable) remains out of scope.
- No change to the persisted state shape or storage key, nor to the reconcile of existing installs; the only service-contract touch is exposing the default-enabled set for the reset-availability check (§Companion change).

---

## References

- Shipped service slice this builds on: `docs/design/service/2026-07-05-lyrics-provider-settings.md`; contract `Hypersonic/Contracts/Services/ILyricsPreferences.cs`.
- In-repo `TokenizingTextBox` precedent (autosuggest + `TokenItemAdding` validation): the UI/Lyrics font pickers in `Hypersonic/Views/SettingsPage.xaml` + `OnFontTokenAdding` / suggestion filtering in `SettingsPage.xaml.cs`.
- TTB source facts relied on: `TokenizingTextBox : ListViewBase`; ItemsPanel `WrapPanel StretchChild="Last"`; `InterspersedObservableCollection` reflects Add/Remove/Reset (not Move) on the bound source — CommunityToolkit/Windows `components/TokenizingTextBox`.
- Package already referenced: `CommunityToolkit.WinUI.Controls.TokenizingTextBox` 8.2.251219 (`Directory.Packages.props`).
- Files touched: `Hypersonic/Controls/ReorderableTokenizingTextBox.cs` (new), `Hypersonic/Views/SettingsPage.xaml` (+ code-behind), `Hypersonic/ViewModels/LyricsSettingsViewModel.cs`, `Hypersonic/ViewModels/LyricsProviderItem.cs`, `Hypersonic/Strings/en-us/Resources.resw`.
- Companion default-set change: `Hypersonic/Services/LyricsPreferences.cs` (default-enabled constant + `ResetProviders`), `Hypersonic.Core/Services/Lyrics/LyricsPreferencesReconciler.cs` (default-enabled append rule), `Hypersonic/Contracts/Services/ILyricsPreferences.cs` (expose default-enabled set); tests in `Hypersonic.Core.Tests` + `Hypersonic.Tests`.
