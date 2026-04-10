# Persistence — Implementation Details

Companion to [docs/design/service/persistence.md](../../design/service/persistence.md). This document records what was actually built, decisions made during implementation, and deviations from the original design.

---

## Files Changed

### New files

| File | Purpose |
|---|---|
| `Hypersonic/Models/ServerAccount.cs` | Non-secret server connection metadata (URL + username) |
| `Hypersonic/Models/AppState.cs` | Hierarchical state model: `PlaybackState`, `WindowState`, `NavigationState` with nested classes |
| `Hypersonic/Contracts/Services/ICredentialService.cs` | Credential storage contract (7 methods) |
| `Hypersonic/Services/CredentialService.cs` | `PasswordVault` + settings-backed credential management with legacy migration |
| `Hypersonic/Contracts/Services/IAppStateService.cs` | App state persistence contract (async saves + `SaveSync`) |
| `Hypersonic/Services/AppStateService.cs` | JSON file-backed state persistence with `SemaphoreSlim` write serialization |
| `Hypersonic/Helpers/VaultKeyHelper.cs` | Vault key derivation helper extracted for testability (`"Hypersonic:{serverUrl}"`) |
| `Hypersonic.UnitTest/PersistenceTests.cs` | 9 pure-logic unit tests (vault key derivation, JSON round-trip, AppState defaults/serialization) |

### Modified files

| File | Changes |
|---|---|
| `Hypersonic/Helpers/Json.cs` | Full rewrite: Newtonsoft.Json → System.Text.Json. Two option sets: `Options` (indented) and `CompactOptions` (single-line for log entries). `JsonStringEnumConverter` configured globally. |
| `Hypersonic/Services/FileService.cs` | Replaced `JsonConvert` calls with `JsonSerializer` using `Json.Options` |
| `Hypersonic/Services/Logging/FileSink.cs` | Replaced `JsonConvert.SerializeObject` with `JsonSerializer.Serialize` using `Json.CompactOptions` to preserve one-entry-per-line log format |
| `Hypersonic/Services/LocalSettingsService.cs` | Added `RemoveSettingAsync` implementation (MSIX + unpackaged); fixed `(string)obj` cast to `obj.ToString()!` for System.Text.Json `JsonElement` compatibility |
| `Hypersonic/Contracts/Services/ILocalSettingsService.cs` | Added `RemoveSettingAsync(string key)` to interface |
| `Hypersonic/Hypersonic.csproj` | Removed `Newtonsoft.Json` package reference |
| `Hypersonic.UnitTest/Hypersonic.UnitTest.csproj` | Removed `Newtonsoft.Json` package reference; added source-links for `Json.cs`, `VaultKeyHelper.cs`, `AppState.cs`, `ServerAccount.cs` |
| `Hypersonic/App.xaml.cs` | Registered `ICredentialService` and `IAppStateService` as singletons |
| `Hypersonic/Services/ActivationService.cs` | Replaced `ILocalSettingsService` credential read with `ICredentialService`; added app state initialization, credential migration, and window geometry restore to startup flow |
| `Hypersonic/Contracts/Services/IPlaybackService.cs` | Added `bool IsMuted { get; set; }` property |
| `Hypersonic/Services/PlaybackService.cs` | Implemented `IsMuted` via `MediaPlayer.IsMuted`; constructor accepts `IAppStateService` and hydrates initial volume/mute/shuffle/repeat from persisted state |
| `Hypersonic/ViewModels/ConnectViewModel.cs` | Replaced `ILocalSettingsService` with `ICredentialService` + `IAppStateService`; `LoadSavedCredentialsAsync` prefills URL/username from settings and password from vault separately; `ConnectAsync` saves via credential service and restores last page |
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Removed `_volumeBeforeMute`; simplified `ToggleMute()` to delegate to service; added state persistence with `_isSavingFromExternal` guard and debounced volume save (1-second `DispatcherQueueTimer`); `FlushPendingSaves()` for close-time sync flush |
| `Hypersonic/MainWindow.xaml.cs` | Added window geometry persistence: `SizeChanged` + `AppWindow.Changed` handlers with 1-second debounce timer; synchronous flush on `Closed` via `SaveSync()`; calls `PlayerViewModel.FlushPendingSaves()` on close |
| `Hypersonic/ViewModels/ShellViewModel.cs` | Injected `IAppStateService`; `OnNavigated` saves `LastPage` for top-level pages only (convention-based Page→ViewModel name mapping) |
| `Hypersonic/Activation/DefaultActivationHandler.cs` | Injected `IAppStateService`; navigates to `LastPage` if it's a valid top-level page, otherwise falls back to `MainViewModel` |

---

## Design Deviations

### `SaveSync()` added to `IAppStateService`

The design spec (Section 2.3) defines only async `Save*Async()` methods on the interface. The implementation adds a synchronous `SaveSync()` method that writes to disk without `Task.Run`.

**Why:** The window close handler runs on the UI thread and the process exits immediately after. A fire-and-forgotten async write would race against process exit — the `Task.Run` file write may not complete before the process terminates. `SaveSync()` writes directly via `File.WriteAllText`, blocking the close handler until the file is flushed.

Note: `SaveSync()` acquires the same `SemaphoreSlim` lock used by async writes, which prevents concurrent file writes. However, this does not fully prevent stale-snapshot overwrites — `WriteStateAsync()` snapshots JSON *before* acquiring the lock, so an in-flight async save holding an older snapshot could still write after `SaveSync()` releases the lock. In practice this is unlikely (the async write would need to be blocked on the lock during close, then resume and overwrite), and the state object is small enough that save frequency makes divergence negligible. A stricter guarantee would require snapshotting inside the lock, at the cost of holding the lock during serialization.

### `VaultKeyHelper` extracted as a separate file

The design spec places vault key derivation as a private method inside `CredentialService`. The implementation extracts it into `Hypersonic/Helpers/VaultKeyHelper.cs` — a public static helper.

**Why:** `CredentialService` depends on `Windows.Security.Credentials.PasswordVault` (WinRT), so it cannot be source-linked into `Hypersonic.UnitTest` (plain `net10.0`). Extracting the pure derivation logic into a separate helper makes it directly testable without WinRT. `CredentialService` delegates to `VaultKeyHelper.GetVaultKey()` internally.

### Last page restoration in both `ConnectViewModel` and `DefaultActivationHandler`

The design spec (Section 2.6) describes last page restoration in `DefaultActivationHandler.HandleInternalAsync()` and mentions that `ConnectViewModel.ConnectAsync()` "should be changed to also check `lastPage`". The implementation does both.

**Why:** Auto-connect (server credential found at startup) flows through `DefaultActivationHandler`, but manual connect (user enters credentials on `ConnectPage`) bypasses it entirely — `ConnectViewModel.ConnectAsync()` directly sets `ShellPage` as content and navigates. Without the check in both paths, last page restoration would only work for auto-connect sessions.

### `TopLevelPages` HashSet duplicated across three files

`ConnectViewModel`, `ShellViewModel`, and `DefaultActivationHandler` each define their own `private static readonly HashSet<string> TopLevelPages`. The design spec does not specify where the page list should live.

**Why:** The set is small (5 entries), compile-time constant, and used in different contexts (save vs. restore). A shared constant would require a new shared location and a dependency for all three classes. The duplication is intentional — changes to navigation pages are infrequent and the set is co-located with its usage for clarity.

### `CredentialService.StorePassword` removes existing credential before adding

The design spec does not specify deduplication behavior for vault writes. The implementation removes any existing `PasswordCredential` for the same resource+username before adding the new one.

**Why:** `PasswordVault.Add()` throws if a duplicate exists. Removing first makes `SaveCredentialAsync` idempotent — calling it twice with updated passwords works correctly without callers needing to distinguish between create and update.

### Mute no longer auto-unmutes on volume change

The design spec (Section 1.3) says mute should be a first-class independent concept via `MediaPlayer.IsMuted`. The previous implementation had `OnVolumePercentChanged` auto-unmute when `value > 0 && IsMuted`. This was removed.

**Why:** With mute decoupled from volume (the volume slider no longer moves to 0 on mute), auto-unmuting on volume change would fight the user — adjusting volume while muted would unmute as a side effect. The user explicitly toggles mute via the mute button; volume changes are independent.

### `UpdateVolumeGlyph` checks `IsMuted` first

The design spec does not specify volume glyph behavior. The previous implementation derived the glyph purely from `VolumePercent`. The new implementation checks `IsMuted` first — if muted, always shows the mute glyph regardless of volume level.

**Why:** With the volume-zeroing mute pattern gone, a user muted at 75% volume would show a volume-3 icon without this check. The mute glyph must reflect the actual audible state.

---

## Implementation Notes

### Write serialization in `AppStateService`

All save paths (async and sync) are serialized through a `SemaphoreSlim(1, 1)`:

- `WriteStateAsync()` snapshots the JSON on the caller's thread (always captures latest `State`), then acquires the lock before the background file write. Uses `.ConfigureAwait(false)` on both the lock acquisition and the `Task.Run` to avoid posting back to the UI thread.
- `SaveSync()` acquires the same lock with `.Wait()`, then writes directly inside the lock.

The lock prevents concurrent file writes but does not fully prevent stale-snapshot overwrites. Because `WriteStateAsync()` serializes `State` *before* acquiring the lock, an async save that serialized an older state can still write that older snapshot after a newer save (or `SaveSync`) completes. In practice this is a minor concern: saves are infrequent (debounced to 1/second for volume and window geometry; immediate but rare for mute/shuffle/repeat), and the state object is small, so the window for divergence is narrow. A stricter guarantee would require moving serialization inside the lock.

Callers fire-and-forget the async saves (`_ = SavePlaybackAsync()`). The lock ensures file writes don't interleave, but the snapshot-before-lock pattern means ordering of *state content* is best-effort, not strict.

### External update guard in `PlayerViewModel`

The `_isSavingFromExternal` flag prevents save-loops when `OnPlaybackStateChanged` hydrates ViewModel properties from service state. Without it, setting `ShuffleEnabled` from the event handler would trigger `OnShuffleEnabledChanged`, which would write back to `AppState` — redundant work since the service already holds the authoritative value.

The flag wraps all property assignments in `OnPlaybackStateChanged()`. The partial methods `OnIsMutedChanged`, `OnShuffleEnabledChanged`, `OnRepeatModeChanged`, and `OnVolumePercentChanged` all check it before persisting.

### Volume debounce

`PlayerViewModel` creates a non-repeating `DispatcherQueueTimer` with a 1-second interval. Each volume slider change stops and restarts the timer. The timer's tick handler writes `VolumePercent` to `AppState` and calls `SavePlaybackAsync()`. On window close, `FlushPendingSaves()` checks `_volumeSaveTimer.IsRunning` — if the timer is pending, it stops it and calls `SaveSync()` to capture the final volume.

Window geometry uses the same pattern: a 1-second non-repeating `DispatcherQueueTimer` in `MainWindow`, flushed synchronously on `Closed`.

### Window geometry restore order

`ActivationService.RestoreWindowState()` always applies saved Width/Height and position first, then maximizes if `IsMaximized` is true. This ensures the window's restore-bounds (the size/position it returns to when un-maximized) are set correctly before the maximize call. Without this order, un-maximizing would use whatever Windows defaulted to, losing the saved placement.

Position validation checks each saved coordinate against all connected `DisplayArea` work areas. If the saved `(X, Y)` falls outside every monitor, the position is skipped and the OS places the window.

### Legacy credential migration

`CredentialService.MigrateFromLegacyAsync()` uses the `"ServerCredential"` settings key as a migration-incomplete sentinel. The four migration steps (add to accounts list, store password in vault, set active account, delete legacy key) are individually guarded with check-then-write:

1. Skip if `"ServerAccounts"` already contains the account
2. Skip if vault already has the password
3. Skip if `"ActiveAccount"` is already set
4. Delete `"ServerCredential"` last

If the app crashes mid-migration, the next launch re-enters the method, finds `"ServerCredential"` still present, and resumes. Steps 1–3 are idempotent by design. The legacy key is only deleted after all other steps succeed.

### Lazy service resolution in `MainWindow`

`MainWindow` is constructed before DI is fully initialized (it's created as a static property in `App.xaml.cs`). `EnsureAppStateService()` defers service resolution to the first `SizeChanged` or `AppWindow.Changed` event, wrapping `App.GetService<IAppStateService>()` in a try-catch. Once resolved, the debounce timer is created and subsequent calls are no-ops.

---

## Verification Results

### Build

`dotnet build Hypersonic.sln -p:Platform=x64` — 0 errors. `dotnet list Hypersonic/Hypersonic.csproj package` confirms no Newtonsoft.Json reference.

### Automated tests

| Test project | Result |
|---|---|
| `Hypersonic.UnitTest` | 89 passed (80 pre-existing + 9 new persistence tests) |
| `Hypersonic.Client.UnitTest` | 32 passed (not affected by this change) |

### New test coverage

| Test | Covers |
|---|---|
| `VaultKeyDerivation_FullUrl` | Full URL → `Hypersonic:https://music.example.com/rest` |
| `VaultKeyDerivation_HttpWithPort` | HTTP with port → `Hypersonic:http://192.168.1.50:4533/` |
| `VaultKeyDerivation_DifferentSchemes_DifferentKeys` | `http://host` ≠ `https://host` |
| `VaultKeyDerivation_PathBasedUrls_DifferentKeys` | `/navidrome1` ≠ `/navidrome2` |
| `JsonHelper_SerializeDeserialize_RoundTrip` | System.Text.Json round-trip for `ServerAccount` |
| `JsonHelper_ReadsLegacyNewtonsoftOutput` | Backward compatibility with Newtonsoft.Json-written JSON |
| `AppState_Defaults` | Fresh `AppState` has correct defaults (volume=100, mute=false, etc.) |
| `AppState_JsonRoundTrip` | Full state round-trip preserves all fields including nested objects |
| `AppState_RepeatMode_SerializesAsString` | `RepeatMode.One` → `"One"` (not `2`) |

### Manual verification

1. Launched with existing `LocalSettings.json` containing `"ServerCredential"` → migrated successfully. `"ServerCredential"` key deleted, `"ServerAccounts"` and `"ActiveAccount"` populated. Password moved to Windows Credential Manager (visible under Web Credentials as `Hypersonic:https://navidrome.ningw.net/rest`).
2. Relaunched → auto-connected via vault (no plain-text password in settings).
3. Changed volume to 52%, closed app, relaunched → volume restored to 52%.
4. Window position and size restored correctly across restarts.
5. Navigated to Albums page, closed, relaunched → Albums page shown.
6. `AppState.json` written with human-readable indented JSON, `RepeatMode` as string.
