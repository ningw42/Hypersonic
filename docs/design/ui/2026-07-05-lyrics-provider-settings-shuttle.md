# Lyrics Provider Settings — dual-list "shuttle" redesign

A **UI-only** redesign of the provider list in **Settings → Lyrics**. The section shipped as a single-column, two-group stacked list (enabled group over a "Disabled" divider over a disabled group), which renders **right-aligned and cramped** — rows squeezed into part of the card, not filling the width. This replaces that block with a **two-column shuttle**: providers move between an **Available** list and an **Enabled** list by drag-and-drop (the WinUI Gallery "drag, drop, and reordering between lists" pattern), which also fixes the width/alignment bug as a side effect.

This builds directly on the shipped provider-settings slice (`docs/design/service/2026-07-05-lyrics-provider-settings.md`). **Only the settings UI and its presentation logic change** — the preferences service, its persistence, live application, and reconcile are all untouched.

No source code — see the forthcoming implementation plan for that.

---

## Scope

Confined to four files plus resource strings:

- **`SettingsPage.xaml`** — the Providers `SettingsCard` body is rebuilt as two side-by-side `ListView`s.
- **`SettingsPage.xaml.cs`** (new code-behind) — the drag-and-drop handlers.
- **`LyricsSettingsViewModel`** — entry points change from "react to a checkbox `IsEnabled` two-way" to a small set of **intent methods**.
- **`LyricsProviderItem`** — loses its checkbox role; gains presentation-only greying flags.
- **`Strings/en-us/Resources.resw`** — column captions, context-menu items, empty-column placeholder.

**Explicitly unchanged:** `ILyricsPreferences` / `LyricsPreferences` (contract and implementation), the reconcile/clamp logic, the persisted state and its storage key, the two budget sliders and their inline resets, the reset-to-defaults behavior, and every downstream consumer (`LyricsService`, `NowPlayingViewModel`, `LyricsFallbackPolicy`).

---

## The decision that keeps this UI-only: the service already expresses everything

The shipped `ILyricsPreferences` contract is sufficient for the shuttle with **no additions**:

- **Enable at a chosen priority** (a left→right drop at index *N*) is `SetEnabled(id, true)` — which appends the provider to the end of the enabled run and already enforces the at-least-one-enabled invariant — followed by `Reorder(enabledIdsInNewOrder)` to slide it to index *N*.
- **Disable** is `SetEnabled(id, false)` (sinks to the disabled run; refuses to drop the count to zero).
- **Reprioritize** is `Reorder(...)`, exactly as today.
- **Reset** is `ResetProviders()`, exactly as today.

Composing enable-at-index from `SetEnabled` + `Reorder` costs two snapshot swaps and two serialized persists per drop. The final persisted state is correct and the existing serialized write queue coalesces the pair, so this is not a concern — and it is far cheaper than growing the service contract for a UI change.

---

## Layout

The Providers `SettingsCard` (`ContentAlignment="Vertical"`) holds a `Grid` of two equal columns (`*,*`), each a captioned `ListView`, with the provider reset icon top-right.

```
┌─ Lyrics providers ─────────────────────────────────  [ ⟳ ] ┐
│                                                              │
│  Available                Enabled — drag to prioritize       │
│  ┌────────────────────┐   ┌──────────────────────────────┐  │
│  │                    │   │ ⠿  Subsonic                  │  │
│  │  All sources       │   │ ⠿  LrcLib                    │  │
│  │  enabled           │   │ ⠿  AMLL                      │  │
│  │  (placeholder)     │   │ ⠿  QQ                        │  │
│  │                    │   │ ⠿  NetEase                   │  │
│  └────────────────────┘   │ ⠿  Kugou                     │  │
│                           └──────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
        first-run state: everything enabled, Available empty
```

- **Two captioned columns** — left **"Available"**, right **"Enabled — drag to prioritize"** (localized).
- **Uniform fixed row height** (~40 px), `MinHeight`-pinned so rows never grow/shrink with content — this is what fixes the original cramping.
- **Fixed, bounded list height** (~200 px, ≈5 rows) with internal scroll, **identical on both columns**, so the card stays a stable size as items shuttle across instead of jumping. With six providers total, scroll rarely triggers; the fixed height exists for stability, not capacity.
- **Row template** — a subtle **grip glyph** (`E76F`) plus the provider `DisplayName` on Enabled rows (signalling "reorderable"); Available rows show just the name (still draggable, but there is no order among them to signal). **No checkbox** — list membership *is* the enabled state.
- **Empty-column placeholder** — faint centered text ("All sources enabled") when a column is empty, so an empty box reads as intentional and remains a valid drop target. The Enabled column can never be empty (invariant), so its placeholder never shows; the Available column is empty on first run and after reset.
- **Reset icon** — `SubtleButtonStyle`, 32 px, glyph `E777`, top-right of the card aligned with the caption row → resets to all-enabled + canonical order (`ResetProvidersCommand`, `CanReset`-gated). Same behavior as today, relocated.

---

## Interactions

Every input — drag, double-tap, context menu — routes through **one set of VM intent methods**, so the input paths never diverge.

### Drag (three directions)

- **Within Enabled** → reorder priority. The built-in `CanReorderItems` mutates the collection and raises `CollectionChanged(Move)` → `Reorder(...)`.
- **Available → Enabled**, dropped at index *N* → enable *at that priority*. The `Drop` handler resolves the insertion index and calls `MoveToEnabled(item, N)`. **This is the only path that lets the user choose the insertion priority.**
- **Enabled → Available** → disable. The `Drop` handler calls `MoveToDisabled(item)`.

### The last-enabled block

When Enabled holds exactly one item, `DragItemsStarting` on the Enabled list **cancels the drag outright** — a single item can neither be reordered nor dragged out, so nothing is lost. Two defensive layers back this up: the Available list refuses the drop (`AcceptedOperation = None`) for the sole enabled item, and `MoveToDisabled` no-ops (the service's `SetEnabled` already refuses a zero-enabled mutation). The primary UX is simply that the drag will not start.

### Non-drag alternatives (keyboard- and mouse-friendly)

- **Double-tap / double-click** a row → move it to the other list. Enabled → disable (no-op on the last enabled); Available → enable, appended at the **end** of Enabled (lowest priority).
- **Context menu** (right-click *and* the keyboard Menu key):
  - **Enabled row:** *Disable* (greyed when it is the last enabled), *Move up*, *Move down* (each greyed at its end of the list).
  - **Available row:** *Enable*.

**Intentional asymmetry:** drag is the only way to enable *at a chosen priority*. Double-tap and the *Enable* menu item add the provider at the end of the Enabled run; the user then drags or *Move up*s it to reprioritize — matching how re-checking behaved in the shipped UI. *Move up / Move down* give keyboard users full priority control, since WinUI `ListView`s do not reorder via keyboard by default.

---

## ViewModel & wiring — explicit, centralized drop handling

Of the three ways to wire drag-and-drop between two lists, the design takes the **explicit-and-centralized** one:

- **Rejected — infer from events:** let the ListView framework mutate both collections and have the VM reconstruct intent from two uncorrelated `CollectionChanged` events (Remove here, Add there). The coupling becomes scattered and after-the-fact, making the last-enabled block and the drop index fragile.
- **Rejected — extract a reusable shuttle behavior:** the same explicit intent as below, lifted into an attached behavior. The shuttle appears on exactly one screen, so the abstraction buys nothing.
- **Chosen — explicit intent methods:** the code-behind `Drop` handler knows *this item, from here, to there, at index N* up front and calls a named method. Intent is stated, not inferred; the fiddly bits live in one place; the VM's move logic is unit-testable without a real `ListView`.

`LyricsSettingsViewModel` keeps its two `ObservableCollection<LyricsProviderItem>` (Enabled / Disabled) and its `_initializing` / `_mutating` guards, and exposes:

- `MoveToEnabled(item, index)` — remove from Disabled, insert into Enabled at `index`; then `SetEnabled(id, true)` followed by `Reorder(enabledIdsInNewOrder)`.
- `MoveToDisabled(item)` — guard the last-enabled case, move to Disabled; `SetEnabled(id, false)`.
- `MoveEnabledUp(item)` / `MoveEnabledDown(item)` — swap within Enabled, then `Reorder(...)`; backs the context-menu items and keyboard priority control.
- `ResetProviders()` — unchanged.

Each intent method raises `_mutating` while it edits the collections and calls the service explicitly, so those VM-driven edits do not re-trigger persistence. The **existing `OnEnabledCollectionChanged → Reorder`** handler stays — it now fires *only* for the built-in intra-list drag reorder; cross-list drops go through `Drop` → the intent methods. That clean split (**within-list = `CollectionChanged(Move)`, cross-list = `Drop`**) is the linchpin of the whole approach.

**`LyricsProviderItem`** loses its checkbox role. `IsEnabled` (two-way) and `CanToggle` are replaced by presentation-only flags the VM recomputes whenever the Enabled list changes, used solely to grey context-menu items: `CanDisable` (Enabled.Count > 1), `CanMoveUp`, `CanMoveDown` (index-based). `Id` / `DisplayName` are unchanged.

---

## Data flow

**Settings open:** `LyricsSettingsViewModel.InitializeAsync` awaits `EnsureLoadedAsync`, then projects the preferences display list into the Enabled / Disabled collections (enabled-first order), exactly as today.

**A move (drag / double-tap / menu):** the input routes to an intent method → the collection edit + explicit `SetEnabled` / `Reorder` calls → snapshot swap → serialized persist. The next fetch reads the new snapshot; nothing else is signalled and no restart is needed — identical to the shipped behavior, only the trigger differs.

---

## Testing

- **VM unit tests** (`Hypersonic.Tests`, against `FakeLyricsPreferences`) — rewritten from "checkbox toggle moves the item" to exercise the intent methods directly:
  - `MoveToEnabled(item, index)` enables at the correct priority (collection order + service calls).
  - `MoveToDisabled` sinks to Available; **no-ops on the last enabled**.
  - `MoveEnabledUp` / `MoveEnabledDown` reorder and persist.
  - The greying flags (`CanDisable`, `CanMoveUp`, `CanMoveDown`) are correct at the list ends and at the last-enabled case.
  - Reset restores canonical order and its `CanReset` gate.
  - Budget-slider tests carry over unchanged.
- **Drag-and-drop itself is UI behavior → manual verification**, not unit-tested (matching how the lyrics-canvas drag work was verified).
- **Service / reconcile tests are untouched** — the service does not change.

---

## Risks / verify in the running app

- **Approach linchpin:** confirm the cross-list `Drop` and the within-list `CollectionChanged(Move)` do not double-fire or double-insert the same item — the framework must not auto-insert the foreign item on a cross-list drop.
- `DragItemsStarting` genuinely blocks the last-enabled drag-out.
- Drop-index math when dropping between rows, at the end of a list, or **onto the empty Available placeholder**.
- Context menu is reachable via the keyboard Menu key; Move-up/down greying is correct at the ends.
- A checkbox-free row still exposes the provider name to the accessibility tree with a sensible automation name.
- Choices persist across a relaunch; the two budget sliders are untouched.

**Manual verification checklist:** drag both directions, reorder within the Enabled list, the last-enabled block, double-tap and right-click move, keyboard Move up/down, reset to defaults, sliders untouched, and choices persisting across a relaunch.

---

## Deferred / out of scope

- Everything the shipped slice deferred (winner-selection changes, per-provider configuration, cache invalidation on disable) remains out of scope.
- No change to the preferences service contract or persistence.
- No reusable/generic shuttle control — the drag logic stays inline to this one screen.

---

## References

- Shipped slice this redesigns: `docs/design/service/2026-07-05-lyrics-provider-settings.md`.
- Files touched: `Hypersonic/Views/SettingsPage.xaml` (+ new code-behind), `Hypersonic/ViewModels/LyricsSettingsViewModel.cs`, `Hypersonic/ViewModels/LyricsProviderItem.cs`, `Hypersonic/Strings/en-us/Resources.resw`.
- Service left unchanged: `Hypersonic/Contracts/Services/ILyricsPreferences.cs`, `Hypersonic/Services/LyricsPreferences.cs`.
- Pattern: WinUI Gallery / community sample "drag, drop, and reordering between lists" (`CanDragItems`, `AllowDrop`, `CanReorderItems`, `DragItemsStarting` / `DragOver` / `Drop`).
