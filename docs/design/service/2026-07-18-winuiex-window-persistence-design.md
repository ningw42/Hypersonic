# WinUIEx-Backed Window Persistence Design

**Date:** 2026-07-18
**Status:** Approved design

## Context

Hypersonic currently owns main-window size, position, and maximized-state persistence. `MainWindow` captures geometry, `AppStateService` stores typed window fields in `AppState.json`, and `ActivationService` restores them before the window activates.

WinUIEx persistence originally existed alongside this implementation. Commit `d3afb3d428f337fc9a52741ad1bf6ba509a3bf03` removed `PersistenceId` because WinUIEx restored its placement during activation and overwrote Hypersonic's earlier manual restore. Removing the competing owner exposed weaknesses in the manual path: it combines DIP dimensions with physical-pixel coordinates and restores while the hidden window is still associated with the wrong DPI.

The design decision is to restore WinUIEx as the sole owner of window-placement semantics while retaining `AppStateService` as the sole owner of durable application-state storage.

## Terminology

- **Placement** — the opaque Base64 blob WinUIEx produces: a monitor-topology signature plus a Win32 `WINDOWPLACEMENT` (size, position, and show-state). The application never decodes it. Persisted as `AppState.Window.Placement`.
- **Geometry** — the previous, now-removed representation: the five typed fields (`Width`, `Height`, `X`, `Y`, `IsMaximized`) the manual path captured and restored. "Geometry" always refers to that legacy model; "placement" always refers to the WinUIEx blob that replaces it.
- **Normal (restore) bounds** — the window's size and position in its non-maximized, non-minimized state (`WINDOWPLACEMENT.rcNormalPosition`). Preserved even while the window is maximized or minimized, so a later un-maximize / un-minimize returns to it.
- **Monitor topology** — the count, arrangement, and physical-pixel rectangles of the connected monitors. WinUIEx records a signature of it with each placement and refuses to restore if it changed.
- **Session vs. launch** — a *session* is one running process; a *launch* is a new process. Persistence spans launches; tray show/hide happens within a session.
- **In-session tray restore vs. cross-session persistence** — reopening the window from the tray is an in-session operation on the live window object; it consults no stored placement. Only a real window close writes placement, and only the first activation of a launch reads it. The two mechanisms are independent.

## Goals

- Persist main-window size, position, normal restore bounds, and maximized state.
- Work in both MSIX-packaged and unpackaged builds.
- Preserve the same physical portion of a monitor when its Windows scaling factor changes between launches.
- Use a safe default placement whenever the monitor topology changes.
- Keep window persistence inside the existing unified `AppState.json` document.
- Eliminate duplicate geometry ownership and the current DPI-fragile restore code.
- Preserve minimize-to-tray and close-to-tray behavior.

## Accepted Behaviors

- WinUIEx may use an opaque Base64 representation of monitor data and `WINDOWPLACEMENT`.
- A crash or force quit may lose the latest placement.
- A canceled close or minimize-to-tray operation does not have to flush placement immediately; a later real window close does.
- A minimized window never reopens minimized. A window minimized from its normal state reopens normal at its saved restore bounds; a window minimized from maximized reopens maximized.
- Changing monitor count, resolution, or arrangement invalidates the placement and uses Windows' safe default.
- The first launch after migrating to this design may use the safe default placement.
- A window saved at its minimum size under a low scaling factor may reopen marginally below the minimum under a higher scaling factor on the same monitor. WinUIEx faithfully preserves the physical rectangle and deliberately does not re-clamp to the minimum during restore (it relaxes the minimum-size constraint while restoring placement); the window is trivially resizable. This is the flip side of preserving the exact physical portion, and is accepted.

## Non-Goals

- Restoring Windows 11 snap groups or snap-layout membership.
- Restoring virtual-desktop membership.
- Restoring fullscreen or compact-overlay presenter state.
- Sharing placement between packaged and unpackaged installations.
- Making the placement blob human-readable or editable.
- General consolidation of `LocalSettingsService` and `AppStateService`.

## Decision

Use WinUIEx `PersistenceId` for capture and restore, but override `WindowManager.PersistenceStorage` with an application-owned adapter in every packaging mode.

The `PersistenceId` value (`"MainWindow"`) is defined once as a shared constant owned by the adapter; `MainWindow` assigns its `PersistenceId` from that constant in code-behind. The adapter composes WinUIEx's storage key as `"WindowPersistance_" + PersistenceId`, so the window's id and the adapter's key are structurally a single source of truth and cannot drift. (`Persistance` is WinUIEx's own misspelling of its key format and must be preserved verbatim.)

The adapter bridges WinUIEx's synchronous `IDictionary<string, object>` contract to `IAppStateService`, exposing only that one entry and mapping its string value to `AppState.Window.Placement`.

This design deliberately does not use WinUIEx's default MSIX `ApplicationData.LocalSettings` container. Packaged and unpackaged builds both flow through `AppStateService`, so their behavior and ownership model remain identical even though each build stores `AppState.json` in a different directory. (WinUIEx consults `PersistenceStorage` first and only falls back to `ApplicationData` when it is null; because the adapter is always assigned, the packaged container is never touched.)

## Ownership and Components

### WinUIEx

WinUIEx owns all interpretation of the placement blob and all Windows placement behavior:

- Capturing and restoring `WINDOWPLACEMENT`.
- Preserving normal bounds when maximized.
- Avoiding a minimized startup state.
- Handling restore-time DPI messages.
- Rejecting placement after a monitor-topology change.
- Falling back safely for missing, corrupt, or incompatible placement.

Application code must never decode or modify the blob.

### AppState-backed persistence adapter

An app-layer `AppStateWindowPersistenceStorage` adapter implements the property-bag contract required by `WindowManager.PersistenceStorage` without exposing WinUIEx to `Hypersonic.Core`. The adapter itself references no WinUIEx types — it implements the BCL `IDictionary<string, object>` interface and is assigned to a WinUIEx static — so it is unit-testable without WinUIEx. Its only dependency is `IAppStateService` (no logger). `WindowManager.PersistenceStorage` holds the adapter for the process lifetime; it does not need a separate DI registration.

Its responsibilities are deliberately narrow:

- Present the expected WinUIEx key — `ContainsKey` returns true and the indexer getter returns the value — only when `AppState.Window.Placement` has a value.
- On an indexer write to that key with a string value, update `AppState.Window.Placement` and call `IAppStateService.SaveSync()`.
- Silently ignore (never throw on) an indexer write with an unknown key or a non-string value, so a stray write can neither mutate other application state nor escape the window-close callback.
- Implement only the members WinUIEx actually invokes — `ContainsKey` and the indexer get/set. The structural mutation and enumeration members (`Add`, `Remove`, `Clear`, `Keys`, `Values`, `TryGetValue`, `GetEnumerator`, `CopyTo`, …) throw `NotSupportedException`; WinUIEx never calls them, and the throw documents that this is not a general-purpose dictionary rather than a second store.
- Own the shared `PersistenceId` constant and compose the WinUIEx key from it.
- Delegate failure logging to `AppStateService`, whose `SaveSync()` already catches and logs I/O errors. The adapter carries no logger of its own; its ignore-branch is unreachable under WinUIEx's actual call pattern.

`SaveSync()` serializes the entire `AppState` document, so a placement write also flushes any pending playback, navigation, and general changes. This is harmless — the service write lock serializes it — but it means a placement mutation is a full-document write, not a window-only write.

The adapter is not a second durable store. It is a protocol adapter over `AppStateService`; `AppState.Window.Placement` is the single source of truth, and the adapter holds no state of its own.

### AppStateService

`AppStateService` remains packaging-agnostic to its consumers and remains the only writer of `AppState.json`.

- MSIX stores the file under `ApplicationData.Current.LocalFolder`.
- Unpackaged builds store it under `%LocalAppData%\Hypersonic\ApplicationData`.
- The existing write lock and synchronous-save path protect the full application-state document.
- Playback, navigation, and general state continue to use the same service unchanged.

`AppStateService` must not reference WinUIEx. The Core model stores only an opaque nullable string.

### ActivationService

After `AppStateService.InitializeAsync()` completes and before `App.MainWindow.Activate()` is called, activation constructs the adapter (`new AppStateWindowPersistenceStorage(_appStateService)` — its only dependency) and assigns it to the static `WindowManager.PersistenceStorage`.

This timing is sufficient because WinUIEx reads persistence on the first `WM_SHOWWINDOW`, not during `MainWindow` construction, and it reads the static lazily inside `LoadPersistence`/`SavePersistence` rather than caching it at `WindowManager` creation. The assignment therefore only has to precede `Activate()`. It also ensures the AppState document is loaded before WinUIEx asks the adapter for the placement entry.

`ActivationService` no longer restores geometry itself.

### MainWindow

`MainWindow` retains its tray behavior and unrelated close cleanup, but no longer owns geometry persistence.

- Assign its `PersistenceId` from the shared constant in code-behind, before activation. The previous `MainWindow.xaml` `PersistenceId` comment and attribute are removed.
- Keep the `IAppStateService` reference — used to read `General.MinimizeToTray` / `CloseToTray` for the tray branches, not for geometry.
- Keep `AppWindow.Changed` handling — still needed to detect the minimize-to-tray transition — but it no longer captures or saves geometry.
- Keep close-to-tray cancellation and real-exit handling.
- Remove size/position capture, the debounce timer, and geometry-specific synchronous flushes. No application state is flushed on hide-to-tray (see Lifecycle).
- Remove geometry event subscriptions that have no remaining consumer.

## Data Model

`WindowState` retains a single nullable `Placement` string. The following fields are removed:

- `Width`
- `Height`
- `X`
- `Y`
- `IsMaximized`

`Placement == null` means that no usable WinUIEx placement exists and Windows should choose the initial placement.

The placement remains nested under the `Window` section of `AppState`, preserving the application's existing state taxonomy even though its representation is opaque.

## Lifecycle

### Startup

1. `AppStateService` resolves the package-appropriate directory and loads `AppState.json`.
2. `ActivationService` constructs the AppState-backed adapter and assigns it to `WindowManager.PersistenceStorage`.
3. `MainWindow` assigns its `PersistenceId` from the shared constant (code-behind) before activation.
4. `App.MainWindow.Activate()` causes the first `WM_SHOWWINDOW`.
5. WinUIEx reads the adapter entry, validates the saved monitor topology, and applies `WINDOWPLACEMENT`.
6. If the entry is absent or invalid, Windows keeps a safe default placement.

### Normal close or explicit tray Exit

1. WinUIEx receives the real window-close event.
2. WinUIEx captures monitor topology and `WINDOWPLACEMENT` and writes its Base64 string into `PersistenceStorage`.
3. The adapter updates `AppState.Window.Placement`.
4. The adapter calls `AppStateService.SaveSync()`.
5. Existing non-window shutdown cleanup continues.

`SaveSync()` writes the entire `AppState` document, and WinUIEx's save runs from `Window.Closed` (subscribed in the `WindowEx` base constructor) before the rest of `MainWindow`'s close cleanup. `PlayerViewModel.FlushPendingSaves()` then issues its own `SaveSync()`. A normal close therefore performs more than one full-document synchronous write; the order is immaterial because each write serializes the complete current model.

### Hide-to-tray

A canceled close or minimize-to-tray transition does not cause WinUIEx to save. If the user later chooses Exit, the real close captures the current placement. If the process is killed while hidden, placement changes made since the previous real close may be lost; this is accepted behavior.

Reopening the window from the tray is an **in-session** operation on the live window object (`Show()`, plus `Restore()` if it was minimized); it consults no stored placement and reads nothing from disk. Persistence is asymmetric with it: only a real close writes placement, and only the first activation of a launch reads it. Consequently no application state is flushed on hide — playback, navigation, and general state each self-persist on change, and losing the latest placement to a kill-while-hidden is already accepted above. The two mechanisms are independent: tray show/hide would behave identically even if persistence were removed entirely.

Minimized state is deliberately normalized during restoration rather than treated as a startup state. WinUIEx restores a window minimized from normal as normal and a window minimized from maximized as maximized. Hypersonic must therefore never appear to launch invisibly merely because the previous session ended while minimized.

## Migration

No geometry conversion is attempted.

The old fields mix DIP dimensions with physical-pixel positions and were captured by a restore path known to be DPI-fragile. Converting them would preserve unreliable semantics and require rebuilding the placement logic this design removes.

On upgrade:

1. The JSON deserializer ignores the removed geometry properties.
2. The new `Placement` property defaults to null.
3. The custom `PersistenceStorage` prevents WinUIEx from reading any stale blob left in its old packaged `LocalSettings` container.
4. Windows chooses a safe default on the first launch.
5. The first real close writes the new placement into `AppState.json`.

No explicit schema version or migration marker is required.

## Failure Handling

- Missing placement: WinUIEx performs no restore.
- Corrupt Base64 or incompatible binary data: WinUIEx catches the load failure and retains the default placement.
- Changed monitor topology: WinUIEx deliberately rejects the placement.
- AppState load failure: the existing service logs the failure and supplies default state, including a null placement.
- AppState save failure: the existing synchronous-save path logs the error and permits shutdown.
- Unexpected adapter key or non-string value: the indexer setter silently ignores it — it cannot alter `WindowState.Placement` or any other AppState property, and it never throws out of the close callback.
- Structural mutation or enumeration on the adapter: throws `NotSupportedException`, but WinUIEx never invokes those members.
- Future WinUIEx format changes: an unreadable blob degrades to a safe default rather than requiring application-side decoding.

## Code Removal and Documentation

The implementation removes the obsolete manual path:

- `ActivationService.RestoreWindowState()`.
- `ActivationService.IsPositionOnScreen()`.
- `MainWindow` geometry capture and debounce state.
- Geometry-only save calls made before hiding or closing.
- The stale `PersistenceId` comment and attribute in `MainWindow.xaml`, replaced by a code-behind assignment from the shared constant.
- `IAppStateService.SaveWindowAsync()` and its implementation/test-double members.
- The five typed geometry properties in `WindowState`.

The temporary root decision brief was retired after implementation. This design remains the durable record that WinUIEx owns placement semantics through an AppState-backed `PersistenceStorage` adapter.

## Verification

### Automated tests

Core model / JSON — in `Hypersonic.Core.Tests` (extending `PersistenceTests`):

- A default `WindowState` has a null placement.
- A placement string round-trips through `AppState` JSON without affecting other state.
- Legacy JSON containing the five removed geometry fields loads with a null placement while retaining unrelated state.

Adapter — in `Hypersonic.Tests` (the adapter has no WinUIEx dependency and is constructed directly against a fake `IAppStateService`):

- `ContainsKey` and the indexer expose the WinUIEx key only when placement exists.
- A string write to the known key changes only `AppState.Window.Placement` and invokes exactly one synchronous save.
- An unknown key or non-string write mutates nothing, triggers no save, and does not throw.
- Structural mutation and enumeration members throw `NotSupportedException`.

Test-double change: `FakeAppStateService` gains a `SaveSyncCount` (so the single-synchronous-save assertions can bind) and loses `SaveWindowAsync` (removed from the interface).

Structural invariant (not an automated test): persistence is configured after `AppStateService.InitializeAsync()` and before `App.MainWindow.Activate()`. This is guaranteed by those calls being sequential in `ActivationService.ActivateAsync` and is verified by code review; asserting line order would require driving the full activation sequence (DI host and a real window) and would test the test rather than the behavior.

Automated tests cover Hypersonic's adapter and lifecycle contract, not WinUIEx's internal `WINDOWPLACEMENT` implementation.

### Manual validation matrix

Run the following for both MSIX and unpackaged builds:

- Close and reopen a normally positioned window.
- Close maximized, reopen maximized, then restore to the previous normal bounds.
- Save on a 4K monitor at 200% scaling, switch to 100%, and verify the same screen portion is retained.
- Save at 100%, switch to 200%, and verify the same screen portion is retained.
- Shrink the window to its minimum on a 100% monitor, relaunch at 200% on that monitor, and confirm it reopens at the same physical size (accepted: it may sit below the DIP minimum until the user resizes it).
- Move between monitors with different DPI values, close, and reopen on the saved monitor.
- Remove, add, rearrange, rotate, or change the resolution of a monitor and verify a visible safe default.
- Minimize to tray, choose Exit, and verify the next launch is normal or maximized rather than minimized.
- Close to tray, choose Exit, and verify the latest normal placement returns.
- Close to tray, reopen from the tray within the same session, and verify the window returns to its pre-hide size and position (in-session restore, no persistence involved).
- Corrupt or remove the placement value and verify a safe default without a startup failure.

## Alternatives Considered

### Keep and repair manual geometry persistence

Rejected because it requires Hypersonic to reproduce Windows placement, DPI, monitor-topology, minimized/maximized, and restore-bounds behavior already handled by WinUIEx.

### Use WinUIEx default storage for MSIX and a file adapter for unpackaged builds

Rejected because it splits window state from the otherwise unified AppState document and creates different ownership paths by packaging mode.

### Use a dedicated WinUIEx file for every build

Rejected because it adds another durable store without improving placement behavior or meeting the preference to keep application state unified.

## Consequences

The design replaces a larger amount of custom geometry code with a small storage adapter. Window placement becomes opaque and coupled to WinUIEx's persistence format, but failures are safe and application code no longer needs to reason about DPI conversion or `WINDOWPLACEMENT`.

`AppState.json` remains the single durable state document for playback, navigation, general state, and window placement. The conceptual boundary is explicit: WinUIEx owns what the placement means; `AppStateService` owns where and when it is durably stored.
