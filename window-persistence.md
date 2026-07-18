# Window Placement Persistence — Resolved Decision

> **Status:** Resolved on 2026-07-18.
>
> **Detailed design:** [WinUIEx-Backed Window Persistence Design](docs/design/service/2026-07-18-winuiex-window-persistence-design.md)

## Decision

Hypersonic uses WinUIEx `PersistenceId` as the sole owner of main-window placement semantics. WinUIEx captures and restores the opaque monitor-topology plus `WINDOWPLACEMENT` blob; Hypersonic does not decode or modify it.

Hypersonic retains `AppStateService` as the sole owner of durable application-state storage. An app-layer `AppStateWindowPersistenceStorage` adapter exposes WinUIEx's `IDictionary<string, object>` protocol and maps `WindowPersistance_MainWindow` to `AppState.Window.Placement` in `AppState.json`.

This is the approved hybrid from the former decision brief: WinUIEx supplies DPI-, monitor-, normal-bounds-, and show-state-aware placement behavior, while the existing app-state document remains the only durable store in both MSIX-packaged and unpackaged builds.

## Ownership

| Concern | Owner |
|---|---|
| Interpret, validate, capture, and restore placement | WinUIEx |
| Persist the opaque placement string | `AppStateService` |
| Bridge WinUIEx's key/value protocol to app state | `AppStateWindowPersistenceStorage` |
| Install storage after app-state load and before activation | `ActivationService` |
| Assign the shared `PersistenceId` and preserve tray behavior | `MainWindow` |

The shared ID is `MainWindow`. WinUIEx's storage key is `WindowPersistance_MainWindow`; the misspelling is part of WinUIEx's protocol.

## Lifecycle

- Startup loads `AppState.json`, assigns `WindowManager.PersistenceStorage`, and then activates the main window. WinUIEx reads placement on the first `WM_SHOWWINDOW`.
- A real close lets WinUIEx capture placement. The adapter writes `AppState.Window.Placement` and synchronously saves the complete state document.
- Minimize-to-tray and close-to-tray keep the live window in-process and do not read or flush placement. A later explicit tray Exit performs a real close and saves placement.
- Missing, corrupt, or monitor-incompatible placement falls back to Windows' safe default.
- A minimized window never launches minimized; WinUIEx restores its normal or maximized restore state.

## Migration

The former `Width`, `Height`, `X`, `Y`, and `IsMaximized` fields are not converted. System.Text.Json ignores them, `Placement` defaults to null, and the first launch after upgrade uses Windows' safe default. The first real close writes a WinUIEx placement blob.

The custom storage adapter is always installed, so packaged builds do not read stale WinUIEx data from `ApplicationData.LocalSettings`.

## Superseded Implementation

The manual capture/restore path in `MainWindow` and `ActivationService` was removed. It mixed DIP dimensions with physical-pixel positions and restored while the hidden window was associated with the wrong DPI. The detailed design linked above records the accepted behavior, failure handling, alternatives, and validation matrix.
