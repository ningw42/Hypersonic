# WinUIEx-Backed Window Persistence Design

**Date:** 2026-07-18
**Status:** Approved design

## Context

Hypersonic currently owns main-window size, position, and maximized-state persistence. `MainWindow` captures geometry, `AppStateService` stores typed window fields in `AppState.json`, and `ActivationService` restores them before the window activates.

WinUIEx persistence originally existed alongside this implementation. Commit `d3afb3d428f337fc9a52741ad1bf6ba509a3bf03` removed `PersistenceId` because WinUIEx restored its placement during activation and overwrote Hypersonic's earlier manual restore. Removing the competing owner exposed weaknesses in the manual path: it combines DIP dimensions with physical-pixel coordinates and restores while the hidden window is still associated with the wrong DPI.

The design decision is to restore WinUIEx as the sole owner of window-placement semantics while retaining `AppStateService` as the sole owner of durable application-state storage.

## Goals

- Persist main-window size, position, normal restore bounds, and maximized state.
- Work in both MSIX-packaged and unpackaged builds.
- Preserve the same physical portion of a monitor when its Windows scaling factor changes between launches, subject to the window's minimum-size constraints.
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

## Non-Goals

- Restoring Windows 11 snap groups or snap-layout membership.
- Restoring virtual-desktop membership.
- Restoring fullscreen or compact-overlay presenter state.
- Sharing placement between packaged and unpackaged installations.
- Making the placement blob human-readable or editable.
- General consolidation of `LocalSettingsService` and `AppStateService`.

## Decision

Use WinUIEx `PersistenceId="MainWindow"` for capture and restore, but override `WindowManager.PersistenceStorage` with an application-owned adapter in every packaging mode.

The adapter bridges WinUIEx's synchronous `IDictionary<string, object>` contract to `IAppStateService`. It exposes only WinUIEx's `WindowPersistance_MainWindow` entry and maps its string value to `AppState.Window.Placement`. The spelling `Persistance` is WinUIEx's existing key format and must be preserved at the adapter boundary.

This design deliberately does not use WinUIEx's default MSIX `ApplicationData.LocalSettings` container. Packaged and unpackaged builds both flow through `AppStateService`, so their behavior and ownership model remain identical even though each build stores `AppState.json` in a different directory.

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

An app-layer `AppStateWindowPersistenceStorage` adapter implements the property-bag contract required by `WindowManager.PersistenceStorage` without exposing WinUIEx to `Hypersonic.Core`. `WindowManager.PersistenceStorage` holds the adapter for the process lifetime; it does not need a separate DI registration.

Its responsibilities are deliberately narrow:

- Present the expected WinUIEx key when `AppState.Window.Placement` has a value.
- Store only string values written to that key.
- Update `AppState.Window.Placement` when WinUIEx captures placement.
- Call `IAppStateService.SaveSync()` after a placement mutation so the close path completes before process exit.
- Prevent unknown keys or value types from mutating other application state.
- Ensure validation or persistence failures do not escape the window-close callback; failures are logged through the existing service behavior and shutdown continues.

The adapter is not a second durable store. It is a protocol adapter over `AppStateService`.

### AppStateService

`AppStateService` remains packaging-agnostic to its consumers and remains the only writer of `AppState.json`.

- MSIX stores the file under `ApplicationData.Current.LocalFolder`.
- Unpackaged builds store it under `%LocalAppData%\Hypersonic\ApplicationData`.
- The existing write lock and synchronous-save path protect the full application-state document.
- Playback, navigation, and general state continue to use the same service unchanged.

`AppStateService` must not reference WinUIEx. The Core model stores only an opaque nullable string.

### ActivationService

After `AppStateService.InitializeAsync()` completes and before `App.MainWindow.Activate()` is called, activation configures the static `WindowManager.PersistenceStorage` property with the adapter.

This timing is sufficient because WinUIEx reads persistence on the first `WM_SHOWWINDOW`, not during `MainWindow` construction. It also ensures the AppState document is loaded before WinUIEx asks the adapter for the placement entry.

`ActivationService` no longer restores geometry itself.

### MainWindow

`MainWindow` retains its tray behavior and unrelated close cleanup, but no longer owns geometry persistence.

- Keep `AppWindow.Changed` handling needed to detect minimize-to-tray.
- Keep close-to-tray cancellation and real-exit handling.
- Remove size/position capture, debounce timers, and geometry-specific synchronous flushes.
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
3. `MainWindow` has `PersistenceId="MainWindow"` configured before activation.
4. `App.MainWindow.Activate()` causes the first `WM_SHOWWINDOW`.
5. WinUIEx reads the adapter entry, validates the saved monitor topology, and applies `WINDOWPLACEMENT`.
6. If the entry is absent or invalid, Windows keeps a safe default placement.

### Normal close or explicit tray Exit

1. WinUIEx receives the real window-close event.
2. WinUIEx captures monitor topology and `WINDOWPLACEMENT` and writes its Base64 string into `PersistenceStorage`.
3. The adapter updates `AppState.Window.Placement`.
4. The adapter calls `AppStateService.SaveSync()`.
5. Existing non-window shutdown cleanup continues.

### Hide-to-tray

A canceled close or minimize-to-tray transition does not cause WinUIEx to save. If the user later chooses Exit, the real close captures the current placement. If the process is killed while hidden, placement changes made since the previous real close may be lost; this is accepted behavior.

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
- Unexpected adapter key or non-string value: it cannot alter `WindowState.Placement` or any other AppState property.
- Future WinUIEx format changes: an unreadable blob degrades to a safe default rather than requiring application-side decoding.

## Code Removal and Documentation

The implementation removes the obsolete manual path:

- `ActivationService.RestoreWindowState()`.
- `ActivationService.IsPositionOnScreen()`.
- `MainWindow` geometry capture and debounce state.
- Geometry-only save calls made before hiding or closing.
- `IAppStateService.SaveWindowAsync()` and its implementation/test-double members.
- The five typed geometry properties in `WindowState`.

`window-persistence.md` should be updated from an open decision brief to a resolved record that points to this design and states that WinUIEx owns placement semantics through an AppState-backed `PersistenceStorage` adapter.

## Verification

### Automated tests

- A default `WindowState` has a null placement.
- A placement string round-trips through AppState JSON without affecting other state.
- Legacy JSON containing the five removed geometry fields loads with a null placement while retaining unrelated state.
- The adapter exposes the expected WinUIEx key only when placement exists.
- A WinUIEx string write changes only `AppState.Window.Placement` and invokes one synchronous save.
- Removing or clearing the supported entry clears placement and saves synchronously.
- Unknown keys and non-string values cannot mutate AppState.
- Persistence is configured after AppState initialization and before main-window activation.

Automated tests cover Hypersonic's adapter and lifecycle contract, not WinUIEx's internal `WINDOWPLACEMENT` implementation.

### Manual validation matrix

Run the following for both MSIX and unpackaged builds:

- Close and reopen a normally positioned window.
- Close maximized, reopen maximized, then restore to the previous normal bounds.
- Save on a 4K monitor at 200% scaling, switch to 100%, and verify the same screen portion is retained.
- Save at 100%, switch to 200%, and verify the same screen portion is retained.
- Move between monitors with different DPI values, close, and reopen on the saved monitor.
- Remove, add, rearrange, rotate, or change the resolution of a monitor and verify a visible safe default.
- Minimize to tray, choose Exit, and verify the next launch is normal or maximized rather than minimized.
- Close to tray, choose Exit, and verify the latest normal placement returns.
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
