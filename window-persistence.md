# Window Geometry Persistence — Decision Brief

> Scope: how Hypersonic should restore the main window's **size, position, and
> maximized state** across launches. Specifically: keep the app's **own**
> `AppState.json`-based implementation, or use **WinUIEx's built-in
> `PersistenceId`**? This is a decision brief for a later deep dive, not a final
> ruling.
>
> Status as of **2026-06-21**: `PersistenceId="MainWindow"` has been removed so
> the manual implementation wins. The change is in the working tree (not
> committed). The dev MSIX was hand-patched for testing — a clean VS deploy is
> still needed. The manual path works but has **DPI/position accuracy bugs**
> (see Evidence), which is what makes this a real decision rather than a
> formality.

---

## TL;DR

- The two systems had been running **simultaneously** for the project's whole
  history. WinUIEx silently won every packaged launch, so the manual restore was
  effectively dead code.
- Removing `PersistenceId` makes the manual `AppState.json` restore authoritative
  — **verified live**.
- But the thing WinUIEx was quietly doing *correctly* (DPI-aware, monitor-aware
  placement) is exactly what the manual path does **poorly** right now. So
  "prefer our own" is currently a trade of *fidelity* for *control + a unified
  store*.
- The deep dive is really about: **do we want window geometry inside
  `AppState.json` (inspectable, integrated with tray-flush / off-screen / other
  state) badly enough to invest in making the manual restore DPI-correct — or is
  WinUIEx's opaque-but-correct store good enough?**

---

## The two implementations

### Option A — WinUIEx `PersistenceId` (built-in)

- **Enabled by:** one XAML attribute, `PersistenceId="MainWindow"`, on the
  `WindowEx` root (`Hypersonic/MainWindow.xaml`).
- **What it stores:** the full Win32 `WINDOWPLACEMENT` (size + position +
  show-state, i.e. maximized/normal) — base64 in `ApplicationData.LocalSettings`,
  container `"WinUIEx"`, key `"WindowPersistance_MainWindow"` (sic — WinUIEx
  misspells "Persistance").
- **When it restores:** on the first `WM_SHOWWINDOW`, via `SetWindowPlacement`.
  In our startup that fires during `App.MainWindow.Activate()` — i.e. **after**
  the manual restore runs.
- **Built-in smarts:** restores via the OS placement API (DPI- and monitor-aware),
  and **invalidates** the restore if the monitor layout changed since save
  (returns early, no restore).
- **Constraints:** packaged-only (needs `ApplicationData.Current`); store is an
  opaque blob in `settings.dat` — not human-readable, not co-located with the
  app's other state.

### Option B — Manual `AppStateService` + `RestoreWindowState` (ours)

- **What it stores:** `WindowState { Width, Height, X, Y, IsMaximized }` as
  plain JSON in `AppState.json`, alongside playback / navigation / general state.
- **Capture:** `MainWindow.xaml.cs:CaptureWindowState()` on `SizeChanged` /
  `AppWindow.Changed`, debounced ~1s; **synchronous** flush on close,
  minimize-to-tray, and close-to-tray (so a process kill while hidden can't lose
  geometry).
- **Restore:** `ActivationService.RestoreWindowState()` — runs **before**
  `Activate()` while the window is still hidden. Sets `Width/Height` (WinUIEx DIP
  setters), `AppWindow.Move(...)` (physical px, gated by an `IsPositionOnScreen`
  off-screen guard), then `presenter.Maximize()` if needed.
- **Why it exists:** deliberate integration — one inspectable `AppState.json`,
  tray-aware sync flushes, off-screen protection, full logging/observability,
  unit-testable.
- **Current weakness:** DPI fragility (see Evidence).

---

## Evidence (live packaged test, 2026-06-21)

Method: seed `AppState.json` with two **different** geometries across two
launches; measure the actual window rect (Win32 `GetWindowRect`) on a 192-DPI
(2.0×) monitor. A window that *tracks* the seed ⇒ `AppState.json` authoritative;
a *constant* window ⇒ WinUIEx overriding.

| Seed (`AppState.json`) | **Before** (PersistenceId present) | **After** (PersistenceId removed) |
|---|---|---|
| `pos(200,150) 960×640` | window `(525,148) 1426×1148` | window `(221,221) 960×1000` |
| `pos(520,330) 1180×520` | window `(525,148) 1426×1148` | window `(245,245) 1180×1000` |

Reading it:

- **Before:** identical window for two different seeds → `AppState.json` ignored,
  WinUIEx's blob wins. (The bug, reproduced.)
- **After:** window **width tracks the seed** (960 → 1180) → manual restore is now
  authoritative. ✅
- **But:** height pinned at ~1000px (= `MinHeight` 500 DIP × 2) for both seeds,
  and position never landed on the saved coords (cascade-like). The manual path
  applies DIP `Width/Height` + `Move` while the window is hidden at the default
  96 DPI, *then* it's shown on a 192-DPI monitor — the saved DIP size collapses,
  collides with `MinWidth/MinHeight` clamping, and position drifts. WinUIEx's
  `SetWindowPlacement` had handled all of this correctly.

---

## Decision axes

| Axis | WinUIEx (A) | Manual (B, today) |
|---|---|---|
| DPI / multi-monitor correctness | ✅ OS placement API | ❌ DIP applied pre-`Activate` at wrong DPI |
| Monitor-layout-changed invalidation | ✅ built-in | ⚠️ only `IsPositionOnScreen` |
| Maximized + restore-bounds | ✅ free (WINDOWPLACEMENT) | ⚠️ hand-rolled (works, but ours) |
| Store is inspectable / unified | ❌ opaque `settings.dat` | ✅ one `AppState.json` |
| Integrates tray-flush / off-screen / logging | ❌ | ✅ |
| App code to maintain | ~none | capture + restore + flush plumbing |
| Works unpackaged | ❌ packaged-only | ✅ |

The tension is essentially **correctness/zero-maintenance (A)** vs
**control/unified-inspectable-store (B)**.

---

## Forward options for the deep dive

- **A. Adopt WinUIEx.** Re-add `PersistenceId`, delete the manual window-geometry
  code (`CaptureWindowState`/`RestoreWindowState`/`WindowState` + the
  geometry-targeting close/minimize flushes). Least code, DPI-correct out of the
  box. Cost: geometry leaves `AppState.json`; lose the custom guards and
  observability; packaged-only.
- **B. Keep manual, fix DPI.** Apply geometry **after** the window is shown /
  `Activate`, and convert DIP↔physical against the **target** monitor's DPI (or
  store physical px and restore with a placement-style call). Keep
  `AppState.json` + tray-flush + off-screen guard. Most work; best fit for the
  app's existing persistence model and the "prefer our own" intent.
- **C. Hybrid.** `AppState.json` stays the store of record, but borrow WinUIEx's
  *technique*: persist a placement (or physical rect + a monitor signature) and
  restore via `SetWindowPlacement` after `Activate`. Best of both; most design
  effort.

---

## Open questions to resolve in the deep dive

1. Do we specifically want window geometry **inside `AppState.json`** (for
   inspection / backup / export / parity with other persisted state), or is an
   opaque OS store acceptable?
2. How much does **mixed-DPI / multi-monitor** correctness matter for the target
   users (vs single 1×/2× display)?
3. Is the **monitor-layout-changed invalidation** (don't restore onto a monitor
   that no longer exists) a requirement? WinUIEx has it; the manual path only has
   the simpler `IsPositionOnScreen` guard.
4. Does **unpackaged** support matter? WinUIEx persistence is packaged-only;
   the manual store works either way.
5. Maximized semantics — "un-maximize returns to the last *normal* size":
   confirm which option gives this most cleanly (WinUIEx gets it for free).
6. Interaction with **tray hide/restore** and the **sync-flush-on-close**
   requirement — these were a primary reason the manual path exists.

---

## Tentative lean (not a decision)

Given the manual system was built deliberately for integration (one
`AppState.json`, tray-aware sync flush, off-screen guard, logging) and the stated
preference to keep "our own implementation," **B (keep manual, fix DPI)** or
**C (hybrid)** fit the app's direction better than reverting to A — *provided*
we're willing to make the restore DPI-correct. If that investment isn't worth it,
**A** is the lowest-effort path to correct behavior, at the cost of the unified
inspectable store.

---

## Pointers

- `Hypersonic/MainWindow.xaml` — where `PersistenceId` lived (now removed, with a
  comment marking why it must not return).
- `Hypersonic/MainWindow.xaml.cs` — `CaptureWindowState()` / `WriteWindowState()`
  + close/minimize/close-to-tray flush plumbing.
- `Hypersonic/Services/ActivationService.cs` — `RestoreWindowState()` and the
  `ActivateAsync` ordering (restore runs **before** `Activate()`).
- `Hypersonic.Core/Models/AppState.cs` — the `WindowState` model.
- WinUIEx mechanics: `WindowManager` `LoadPersistence`/`SavePersistence`
  (`SetWindowPlacement` on first `WM_SHOWWINDOW`; LocalSettings container
  `"WinUIEx"`).
- Deploy caveat: `dotnet build` does **not** refresh the registered MSIX layout
  (`bin\...\win-x64\AppX`); use VS F5 (Deploy) to test packaged behavior.
