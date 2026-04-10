# Persistence — Feature Spec & Design Spec

## Context

Hypersonic currently persists three key-value pairs through `ILocalSettingsService`: a `ServerCredential` object (server URL, username, and password as plain-text JSON), a theme preference, and a backdrop preference. All three share a single flat `Dictionary<string, object>` serialized with Newtonsoft.Json.

This design has three problems:

1. **Credentials are stored in plain text.** The password sits unencrypted in `LocalSettings.json` (unpackaged) or `ApplicationData.Current.LocalSettings` (MSIX). Microsoft's guidance is explicit: *"Never store credentials in plain-text using app data or roaming settings."*
2. **No player state survives restart.** Volume, shuffle, repeat, mute, window geometry, and last-viewed page are all ephemeral. Users start fresh every launch.
3. **Single server assumption.** The current `ServerCredential` model stores exactly one server. There is no foundation for remembering previously connected servers or switching between them.

The roadmap (item 1.2) specifies two persistence categories: **Settings** and **State**. This design adds a third category — **Credentials** — because credentials have fundamentally different security requirements and should not share a storage mechanism with preferences or runtime state.

---

## Part 1: Feature Spec

### 1.1 Credentials

Passwords are stored in the Windows Credential Manager via `PasswordVault`, encrypted by the OS and tied to the Windows user account. Non-secret connection metadata (server URL, username) remains in settings.

- The app remembers all servers the user has connected to (as a list in settings), not just the active one.
- Only one server is active at a time. Quick-switch UI is deferred; the data model supports it from day one.
- The vault resource key uses the **full normalized server URL**: `Hypersonic:<serverUrl>`. This is the same identity used in settings (`ServerAccount.ServerUrl` + `Username`), ensuring a single canonical account identity across all persistence layers.
- `http://host` and `https://host` are treated as **distinct server entries**, even if the underlying Subsonic password happens to be the same. This avoids identity mismatches between the settings layer and the credential store, and simplifies migration, multi-server, and "forget server" logic.
- Connecting to a new server adds it to the known servers list and stores its password in the vault.
- Disconnecting from a server does not remove it from the known servers list or the vault. An explicit "Forget server" action (future UI) does both.

### 1.2 Settings

User preferences persisted via the existing `ILocalSettingsService` key-value store. It continues to use `ApplicationData.Current.LocalSettings` for MSIX and a JSON file for unpackaged builds. Two additions: a `RemoveSettingAsync` method (Section 2.4) and a minor fix to the unpackaged deserialization path for System.Text.Json compatibility (Section 2.5).

Currently persisted settings (unchanged):

| Key | Type | Purpose |
|---|---|---|
| `"AppBackgroundRequestedTheme"` | `string` | Light / Dark / Default |
| `"AppRequestedBackdrop"` | `string` | None / Mica / MicaAlt / Acrylic |

New settings:

| Key | Type | Purpose |
|---|---|---|
| `"ServerAccounts"` | `ServerAccount[]` | List of known servers (URL + username) |
| `"ActiveAccount"` | `ServerAccount` | The currently active server (URL + username), or null |

The active account is identified by the `(ServerUrl, Username)` pair — the same identity used in the vault resource key (`Hypersonic:<serverUrl>` + username). A single canonical identity model across settings and credentials keeps the persistence layer simple and unambiguous. `"ActiveAccount"` is stored as a single composite setting (not two separate keys) so it can be written and read atomically — a crash between two separate key writes cannot leave the active account in a half-set state.

The `"ServerCredential"` key is retired. Its data is migrated on first launch: the URL and username move to `"ServerAccounts"`, the password moves to `PasswordVault`, and the old key is deleted. Deletion requires adding `RemoveSettingAsync` to `ILocalSettingsService` (see Section 2.4).

### 1.3 Mute/Volume Model Alignment (Prerequisite)

The persistence schema treats `volume` and `isMuted` as independent fields. However, the current implementation conflates them: `PlayerViewModel.ToggleMute()` sets `VolumePercent = 0` on mute and restores from `_volumeBeforeMute` on unmute (`PlayerViewModel.cs:163-176`). The `IPlaybackService` interface has no mute property — it only exposes `Volume` (`IPlaybackService.cs:14`).

This design **requires** that mute be made a first-class concept before state persistence is wired:

1. Add `bool IsMuted { get; set; }` to `IPlaybackService`.
2. In `PlaybackService`, implement `IsMuted` by setting `MediaPlayer.IsMuted` (the native player already supports this) rather than zeroing the volume.
3. In `PlayerViewModel`, simplify `ToggleMute()` to toggle `_playbackService.IsMuted` — no need to save/restore `_volumeBeforeMute`.

This is a small, self-contained refactor that must land before or alongside step 8 (playback state wiring) in the implementation sequence.

### 1.4 State

Server-agnostic runtime state persisted in a separate JSON file (`AppState.json`) so the app can restore its last session on relaunch. This is distinct from settings: state changes frequently, is not user-facing, and is not shown on a Settings page.

| State | Default | Write trigger |
|---|---|---|
| `playback.volume` | `100` | On change (debounced) + on close |
| `playback.isMuted` | `false` | On change |
| `playback.shuffleEnabled` | `false` | On change |
| `playback.repeatMode` | `"Off"` | On change |
| `window.width` | (startup default) | On resize (debounced) + on close |
| `window.height` | (startup default) | On resize (debounced) + on close |
| `window.x` | (OS default) | On move (debounced) + on close |
| `window.y` | (OS default) | On move (debounced) + on close |
| `window.isMaximized` | `false` | On state change + on close |
| `navigation.lastPage` | `null` | On navigate (top-level pages only) |

`playback.repeatMode` uses the `RepeatMode` enum (`Hypersonic/Models/RepeatMode.cs`). It is serialized as a string (`"Off"`, `"All"`, `"One"`) via the `JsonStringEnumConverter` configured in `Json.Options` (see Section 2.5).

**Server-tied state** (queue contents, current track, playback position, queue origin) is **not included** in this design. It will be addressed in a future design that accounts for per-server state partitioning.

**Navigation scope**: Only **top-level parameterless pages** are persisted: Home, Albums, Artists, Playlists, Settings. Detail pages (AlbumDetail, ArtistDetail, PlaylistDetail) require a navigation parameter (server-specific ID) to function, making them server-tied state — explicitly out of scope per Section 1.4. If the last page was a detail page, `lastPage` is not updated; on restore, the app navigates to the most recent top-level page instead. This avoids the need to persist navigation parameters while still providing useful session restoration for the common case.

**Write frequency**: Discrete playback preferences (mute, shuffle, repeat) and navigation state are written immediately on change (they change infrequently — a few times per session). **Volume is debounced** (write at most once per second) because the slider generates continuous change events during a drag gesture — writing on every tick would produce a burst of full-file writes. Window geometry is also debounced (write at most once per second during resize/move). Both volume and window geometry are always flushed on window close to capture the final state.

**Read frequency**: Once, at app startup.

### 1.5 Serialization Migration

The project currently uses Newtonsoft.Json (`JsonConvert`) for all serialization: `Helpers/Json.cs`, `Services/FileService.cs`, and `Services/Logging/FileSink.cs` (context serialization in log entries). This design migrates all three call sites to `System.Text.Json`, which is built into .NET 10 and is Microsoft's recommended serializer. This eliminates the Newtonsoft.Json dependency from the app project entirely.

**Note**: The `Hypersonic.UnitTest` project also has a Newtonsoft.Json dependency (for `FileSink.cs` via source-link). After the app migration, this test project dependency can be removed as well.

### 1.6 Roadmap Alignment

The roadmap (item 1.2) currently describes two persistence categories: Settings and State. This design adds Credentials as a third category. The roadmap description should be updated to reflect the three-bucket architecture once this design is approved.

### 1.7 Not In Scope

- Quick-switch server UI (data model supports it; UI is deferred).
- "Forget server" UI for removing saved credentials.
- Per-server state (queue, play history, scrobble queue).
- SQLite for state storage (adequate for Phase 3 stretch goals, not needed here).
- User-editable config files (all persistence is machine-managed via UI).

---

## Part 2: Design Spec

### 2.1 New Models

**`ServerAccount`** — new file: `Hypersonic/Models/ServerAccount.cs`

```csharp
namespace Hypersonic.Models;

public class ServerAccount
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}
```

Non-secret connection metadata. The password is stored separately in `PasswordVault`. `ServerUrl` is the full resolved URL with scheme (e.g., `https://music.example.com/rest`).

**`AppState`** — new file: `Hypersonic/Models/AppState.cs`

```csharp
namespace Hypersonic.Models;

public class AppState
{
    public PlaybackState Playback { get; set; } = new();
    public WindowState Window { get; set; } = new();
    public NavigationState Navigation { get; set; } = new();
}

public class PlaybackState
{
    public double Volume { get; set; } = 100;
    public bool IsMuted { get; set; }
    public bool ShuffleEnabled { get; set; }
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
}

public class WindowState
{
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool IsMaximized { get; set; }
}

public class NavigationState
{
    public string? LastPage { get; set; }
}
```

Nullable window fields mean "use OS/startup default" — the app does not override window placement on first launch.

### 2.2 Credential Service

**`ICredentialService`** — new file: `Hypersonic/Contracts/Services/ICredentialService.cs`

```csharp
namespace Hypersonic.Contracts.Services;

public interface ICredentialService
{
    Task<IReadOnlyList<ServerAccount>> GetServerAccountsAsync();
    Task<ServerAccount?> GetActiveServerAccountAsync();
    Task<ServerCredential?> GetActiveCredentialAsync();
    Task SaveCredentialAsync(ServerCredential credential);
    Task SetActiveServerAsync(string serverUrl, string username);
    Task RemoveCredentialAsync(string serverUrl, string username);
    Task MigrateFromLegacyAsync();
}
```

**`CredentialService`** — new file: `Hypersonic/Services/CredentialService.cs`

Responsibilities:
- Manages the `"ServerAccounts"` and `"ActiveAccount"` settings via `ILocalSettingsService`.
- Reads/writes passwords via `Windows.Security.Credentials.PasswordVault`.
- Derives the vault resource key from a server URL: `"Hypersonic:<serverUrl>"`. The full normalized URL is used as-is — no scheme stripping or host extraction. This ensures the vault key matches the `ServerAccount.ServerUrl` identity exactly.
- `GetActiveCredentialAsync()` reconstructs a full `ServerCredential` (URL + username + password) by reading the `"ActiveAccount"` setting, and retrieving the password from the vault using the derived vault key + username.

**Vault key derivation**:

```
Input:  https://music.example.com/rest
Output: Hypersonic:https://music.example.com/rest

Input:  http://192.168.1.50:4533/
Output: Hypersonic:http://192.168.1.50:4533/
```

Note: `http://host` and `https://host` produce **different** vault keys. They are distinct server entries. If a user switches transport scheme, they will be prompted to re-enter credentials — a minor usability trade-off for a consistent, unambiguous identity model.

The `PasswordCredential` is stored as `(resource: vaultKey, userName: username, password: password)`.

**Legacy migration** (`MigrateFromLegacyAsync`):
1. Read `"ServerCredential"` from `ILocalSettingsService`. If not found: no-op, return.
2. If found, perform each step only if not already completed (check before write), making the migration resumable after partial failure:
   a. If `"ServerAccounts"` does not contain an entry matching URL + username: add a new `ServerAccount`.
   b. If `PasswordVault` has no credential for the derived vault key + username: store the password.
   c. If `"ActiveAccount"` is not set: save it as a single composite `ServerAccount` value.
   d. Delete `"ServerCredential"` via `ILocalSettingsService.RemoveSettingAsync()`.
3. The `"ServerCredential"` key is deleted last — it acts as the migration-incomplete sentinel. If the app crashes mid-migration, the next launch will re-enter this method, find `"ServerCredential"` still present, and resume from wherever it left off. Steps a–c are individually idempotent (check-then-write), and step c is atomic (single key write), so re-running them is safe.

### 2.3 App State Service

**`IAppStateService`** — new file: `Hypersonic/Contracts/Services/IAppStateService.cs`

```csharp
namespace Hypersonic.Contracts.Services;

public interface IAppStateService
{
    AppState State { get; }
    Task InitializeAsync();
    Task SaveAsync();
    Task SavePlaybackAsync();
    Task SaveWindowAsync();
    Task SaveNavigationAsync();
}
```

**`AppStateService`** — new file: `Hypersonic/Services/AppStateService.cs`

Responsibilities:
- On `InitializeAsync()`: load `AppState.json` from the application data folder (same base folder as `LocalSettings.json`, i.e., `%LOCALAPPDATA%/Hypersonic/ApplicationData/`). If the file doesn't exist, use defaults.
- Exposes `State` as a mutable in-memory object. Callers modify properties directly, then call the appropriate `Save*Async()` method.
- Each `Save*Async()` method writes the full `AppState` object to disk. Writing the full object (rather than partial updates) keeps the implementation simple and the file consistent. The state object is small (~200 bytes JSON) so full-file writes are negligible.
- Uses `System.Text.Json.JsonSerializer` directly — does not go through `ILocalSettingsService`, which is designed for flat key-value pairs, not hierarchical documents.

**File location**:
- MSIX: `ApplicationData.Current.LocalFolder` + `AppState.json`
- Unpackaged: `%LOCALAPPDATA%/Hypersonic/ApplicationData/AppState.json`

The same `RuntimeHelper.IsMSIX` check used by `LocalSettingsService` determines the base path.

**Example `AppState.json`**:

```json
{
    "Playback": {
        "Volume": 75,
        "IsMuted": false,
        "ShuffleEnabled": true,
        "RepeatMode": "All"
    },
    "Window": {
        "Width": 1200,
        "Height": 800,
        "X": 100,
        "Y": 100,
        "IsMaximized": false
    },
    "Navigation": {
        "LastPage": "Hypersonic.ViewModels.AlbumsViewModel"
    }
}
```

Note: Property names are PascalCase, matching System.Text.Json's default output (no `PropertyNamingPolicy` set). `PropertyNameCaseInsensitive = true` ensures deserialization works regardless of casing.

### 2.4 Settings Service Extension

**`ILocalSettingsService`** — add `RemoveSettingAsync` to the existing interface:

```csharp
namespace Hypersonic.Contracts.Services;

public interface ILocalSettingsService
{
    Task<T?> ReadSettingAsync<T>(string key);
    Task SaveSettingAsync<T>(string key, T value);
    Task RemoveSettingAsync(string key);
}
```

**`LocalSettingsService`** — implement `RemoveSettingAsync`:
- MSIX: `ApplicationData.Current.LocalSettings.Values.Remove(key)`.
- Unpackaged: remove the key from `_settings` dictionary, then persist via `FileService.Save()`.

This is needed for legacy migration (deleting `"ServerCredential"`) and for any future "forget server" functionality. The existing `IFileService` already has a `Delete()` method, but key-level removal within the settings dictionary is the correct granularity here.

### 2.5 Serialization Migration (Newtonsoft.Json to System.Text.Json)

**`Helpers/Json.cs`** — rewrite to use `System.Text.Json`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypersonic.Helpers;

public static class Json
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<T?> ToObjectAsync<T>(string value)
    {
        return await Task.Run(() => JsonSerializer.Deserialize<T>(value, _options));
    }

    public static async Task<string> StringifyAsync(object value)
    {
        return await Task.Run(() => JsonSerializer.Serialize(value, _options));
    }

    public static JsonSerializerOptions Options => _options;
}
```

**Naming policy**: No `PropertyNamingPolicy` is set, so System.Text.Json writes PascalCase property names by default — matching the existing Newtonsoft.Json output (which also defaults to PascalCase since no `CamelCasePropertyNamesContractResolver` is configured). `PropertyNameCaseInsensitive = true` ensures deserialization works regardless of casing, providing resilience against any future naming changes.

**Enum handling**: `JsonStringEnumConverter` serializes enums as their string names (e.g., `"Off"`, `"All"`, `"One"`) rather than integer values. This is used by `AppState.PlaybackState.RepeatMode` and any log context objects containing enums. This matches Newtonsoft.Json's default enum-as-integer behavior only for simple enums — but string representation is preferred for human-readable config files and forward compatibility.

**`Services/FileService.cs`** — replace `JsonConvert` with `JsonSerializer`. Additionally, `FileService.Read<T>` must handle a System.Text.Json behavioral difference: when deserializing `IDictionary<string, object>`, Newtonsoft.Json returns `string` values, but System.Text.Json returns `JsonElement` values. The `LocalSettingsService` unpackaged path reads `IDictionary<string, object>` and casts values to `string` (`LocalSettingsService.cs:66,76`). To fix this, `LocalSettingsService.ReadSettingAsync` must call `.ToString()` on the dictionary value (which works for both `string` and `JsonElement`) rather than a direct `(string)` cast. This change is scoped to `LocalSettingsService`, not `FileService` — `FileService` remains a generic serializer.

**`Services/Logging/FileSink.cs`** — replace `JsonConvert.SerializeObject(entry.Context)` (line 35) with `JsonSerializer.Serialize(entry.Context, Json.CompactOptions)`. This must use **compact** (non-indented) serialization to preserve the one-entry-per-line log format required by the logging design (`logging-service.md:66`). Add a separate `CompactOptions` to `Json.cs`:

```csharp
public static readonly JsonSerializerOptions CompactOptions = new()
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = false,
    Converters = { new JsonStringEnumConverter() },
};
```

**`Hypersonic.csproj`** — remove the `Newtonsoft.Json` package reference after migration.

**`Hypersonic.UnitTest.csproj`** — remove the `Newtonsoft.Json` package reference (was only needed because `FileSink.cs` is source-linked into this project).

**Backward compatibility**: The new `System.Text.Json` serializer must be able to read existing settings files written by Newtonsoft.Json. The current Newtonsoft.Json configuration uses default settings (no `CamelCasePropertyNamesContractResolver`), so existing serialized objects use **PascalCase** property names (e.g., `{"ServerUrl":"...","Username":"...","Password":"..."}`). The proposed `Json.Options` omits `PropertyNamingPolicy`, so System.Text.Json also writes PascalCase by default — matching existing output. `PropertyNameCaseInsensitive = true` provides additional resilience during deserialization. Existing simple string settings (`"AppBackgroundRequestedTheme"`, `"AppRequestedBackdrop"`) are stored as quoted JSON strings and will deserialize identically under both serializers.

### 2.6 Integration Points

**`ActivationService.ActivateAsync()`** — modified startup flow:

```
1. InitializeAsync()          — theme + backdrop (existing)
                              — app state (new: load AppState.json)
                              — credential migration (new: MigrateFromLegacyAsync)
2. Restore window state       — ActivationService applies AppState.Window to MainWindow BEFORE setting content (new)
3. Load credential            — via ICredentialService.GetActiveCredentialAsync() (replaces direct LocalSettings read)
4. Test connection + show UI  — (existing logic, unchanged)
5. Activate window             — (existing)
6. StartupAsync()             — theme + backdrop apply (existing)
```

Window state is restored in step 2 — before any content is set — so the window is correctly sized regardless of whether the user lands on ConnectPage or ShellPage. This avoids a visible resize after the window is shown.

**`ConnectViewModel`** — modified to use `ICredentialService`:
- `LoadSavedCredentialsAsync()`: calls `ICredentialService.GetActiveServerAccountAsync()` to prefill server URL and username from settings (always available). Then attempts `ICredentialService.GetActiveCredentialAsync()` to retrieve the password from the vault. If vault retrieval fails (returns `null`), the URL and username fields are still populated — only the password field is left empty, prompting the user to re-enter it. This is a graceful degradation: non-secret metadata is never lost due to a vault failure.
- `ConnectAsync()`: after a successful connection test, uses `result.ResolvedUrl` (the normalized URL) — not the raw user input — when calling `ICredentialService.SaveCredentialAsync(credential)` and `ICredentialService.SetActiveServerAsync(result.ResolvedUrl, username)`. This matches the existing pattern at `ConnectViewModel.cs:166` where `credential.ServerUrl = result.ResolvedUrl` is set before saving. Using the resolved URL as the canonical identity prevents duplicate accounts caused by URL variations (e.g., `music.example.com` vs `music.example.com/rest`).

**`PlayerViewModel`** — saves playback state on change:
- `OnIsMutedChanged()`, `OnShuffleEnabledChanged()`, `OnRepeatModeChanged()`: update `IAppStateService.State.Playback.*` and call `SavePlaybackAsync()` immediately (these are discrete, infrequent changes).
- `OnVolumePercentChanged()`: update `IAppStateService.State.Playback.Volume` and call `SavePlaybackAsync()` **debounced** (at most once per second) — the volume slider fires continuously during drag. Use a `DispatcherQueueTimer` or similar mechanism; flush on window close.
- On initialization: hydrate `VolumePercent`, `IsMuted` from `IAppStateService.State.Playback` (shuffle and repeat are already hydrated from `IPlaybackService`; the service itself should read initial values from `AppState`).

**`PlaybackService`** — reads initial shuffle/repeat from `IAppStateService.State.Playback` on construction, so the queue starts with the user's last-used modes.

**Window state** — restoration and persistence are split across two locations:
- **Restore** (`ActivationService.ActivateAsync`, step 2): Apply `AppState.Window` (size, position, maximized state) to `App.MainWindow` during step 2 of the startup flow, before content is set and before `Window.Activate()`. This ensures both ConnectPage and ShellPage launches get the correct geometry. Validate that the saved position is still on a connected monitor before applying; fall back to OS default if off-screen.
- **Save** (`MainWindow`): Subscribe to `SizeChanged` / position changes in `MainWindow.xaml.cs` and call `SaveWindowAsync()` debounced. Always flush on `MainWindow.Closed`. This is at the window layer so it works regardless of which page is shown.

**Navigation state** — hook the `Navigated` event (not just `NavigateTo()`) to capture all navigation changes including back-stack navigation (`GoBack()`):
- Subscribe to `INavigationService.Navigated` in `ShellPage` or `ShellViewModel`.
- On each `Navigated` event: check if the destination page is a top-level parameterless page (Home, Albums, Artists, Playlists, Settings). If so, update `AppState.Navigation.LastPage` with the ViewModel's full name and call `SaveNavigationAsync()`. If the destination is a detail page (requires a parameter), do not update `lastPage`.
- **Restoration**: To use `lastPage` on startup, modify `DefaultActivationHandler.HandleInternalAsync()` (`DefaultActivationHandler.cs:23-25`): instead of unconditionally navigating to `MainViewModel`, check `IAppStateService.State.Navigation.LastPage` first. If it names a valid top-level page, navigate there; otherwise fall back to `MainViewModel`. This single change covers both auto-connect (where `DefaultActivationHandler` fires via `ActivationService.ShowShell`) and manual connect (where `ConnectViewModel.ConnectAsync()` at line 175 calls `navigationService.NavigateTo(typeof(MainViewModel).FullName!)` — this should be changed to also check `lastPage`).

### 2.7 DI Registration

In `App.xaml.cs`:

```csharp
// New registrations (singletons)
services.AddSingleton<ICredentialService, CredentialService>();
services.AddSingleton<IAppStateService, AppStateService>();
```

### 2.8 Error Handling

- **`PasswordVault` not found / empty**: `vault.Retrieve()` throws if no matching credential exists. Catch and return `null` — treat as "no saved password", prompt user to re-enter.
- **`AppState.json` corrupt / unreadable**: Log warning, use defaults. Do not crash on state restoration failure.
- **Window position off-screen**: If saved `(X, Y)` places the window entirely outside all connected monitors, ignore saved position and let the OS place the window. Check via `DisplayArea` enumeration.
- **Migration failure**: Log error, continue with empty credential state. User will see the connect page and can re-enter credentials.

### 2.9 Backward Compatibility

- On first launch after this change, `MigrateFromLegacyAsync()` transparently moves the existing `"ServerCredential"` to the new storage. The user experiences no disruption — auto-connect works as before.
- If `"ServerCredential"` key does not exist (fresh install or already migrated), migration is a no-op.
- If a previous migration was interrupted (e.g., `"ServerAccounts"` exists but `"ServerCredential"` was not yet deleted), the next run resumes from where it left off — each step is individually guarded and idempotent.
- Theme and backdrop settings are untouched — same keys, same format, same `ILocalSettingsService` calls.
- System.Text.Json reads existing Newtonsoft.Json-written settings transparently — both produce PascalCase JSON by default. `PropertyNameCaseInsensitive = true` adds resilience. `JsonStringEnumConverter` ensures enums serialize as readable strings in `AppState.json`.

---

## Files to Create

| File | Purpose |
|---|---|
| `Hypersonic/Models/ServerAccount.cs` | Non-secret server connection metadata |
| `Hypersonic/Models/AppState.cs` | Hierarchical state model (playback, window, navigation) |
| `Hypersonic/Contracts/Services/ICredentialService.cs` | Credential storage contract |
| `Hypersonic/Services/CredentialService.cs` | PasswordVault + settings-backed credential management |
| `Hypersonic/Contracts/Services/IAppStateService.cs` | App state persistence contract |
| `Hypersonic/Services/AppStateService.cs` | JSON file-backed state persistence |

## Files to Modify

| File | Changes |
|---|---|
| `Hypersonic/Contracts/Services/ILocalSettingsService.cs` | Add `RemoveSettingAsync` method |
| `Hypersonic/Services/LocalSettingsService.cs` | Implement `RemoveSettingAsync`; fix `(string)` cast to `.ToString()` for System.Text.Json `JsonElement` compatibility on unpackaged path |
| `Hypersonic/Helpers/Json.cs` | Replace Newtonsoft.Json with System.Text.Json |
| `Hypersonic/Services/FileService.cs` | Replace JsonConvert with JsonSerializer |
| `Hypersonic/Services/Logging/FileSink.cs` | Replace JsonConvert with JsonSerializer |
| `Hypersonic/Hypersonic.csproj` | Remove Newtonsoft.Json package reference |
| `Hypersonic.UnitTest/Hypersonic.UnitTest.csproj` | Remove Newtonsoft.Json package reference |
| `Hypersonic/App.xaml.cs` | Register `ICredentialService`, `IAppStateService` |
| `Hypersonic/Services/ActivationService.cs` | Add credential migration + app state init to startup; restore window geometry from `AppState` before setting content |
| `Hypersonic/Contracts/Services/IPlaybackService.cs` | Add `bool IsMuted { get; set; }` property |
| `Hypersonic/Services/PlaybackService.cs` | Implement `IsMuted` via `MediaPlayer.IsMuted`; read initial shuffle/repeat/volume/mute from `IAppStateService` |
| `Hypersonic/ViewModels/ConnectViewModel.cs` | Use `ICredentialService` instead of direct settings; navigate to `lastPage` instead of hardcoded `MainViewModel` |
| `Hypersonic/Activation/DefaultActivationHandler.cs` | Navigate to `lastPage` from `IAppStateService` instead of hardcoded `MainViewModel` |
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Simplify mute to delegate to service; save/restore playback state via `IAppStateService` |
| `Hypersonic/MainWindow.xaml.cs` | Save window geometry on resize/move (debounced) + on close; restore is handled by `ActivationService` |

## Files Unchanged but Referenced

| File | Role |
|---|---|
| `Hypersonic/Models/ServerCredential.cs` | Retained as internal aggregate (URL + username + password) for connection flow |
| `Hypersonic/Models/RepeatMode.cs` | Used by AppState.PlaybackState |
| `Hypersonic/Helpers/RuntimeHelper.cs` | MSIX detection for file path resolution |
| `Hypersonic/Services/PageService.cs` | Navigation key format: `typeof(VM).FullName!` (e.g., `Hypersonic.ViewModels.AlbumsViewModel`) |

---

## Implementation Sequence

1. **Serialization migration**: Rewrite `Json.cs`, `FileService.cs`, and `FileSink.cs` to use `System.Text.Json`. Remove Newtonsoft.Json from `Hypersonic.csproj` and `Hypersonic.UnitTest.csproj`. Verify existing settings still deserialize correctly.
2. **Settings service extension**: Add `RemoveSettingAsync` to `ILocalSettingsService` and `LocalSettingsService`.
3. **New models**: `ServerAccount`, `AppState` (with `PlaybackState`, `WindowState`, `NavigationState`).
4. **Credential service**: `ICredentialService` + `CredentialService`. Implement `PasswordVault` integration, vault key derivation, server accounts list management, legacy migration.
5. **App state service**: `IAppStateService` + `AppStateService`. Implement JSON file read/write with `System.Text.Json`.
6. **DI registration**: Register both new services in `App.xaml.cs`.
7. **Activation integration**: Wire credential migration and app state initialization into `ActivationService`.
8. **ConnectViewModel migration**: Switch from `ILocalSettingsService` to `ICredentialService`.
9. **Mute/volume refactor**: Add `IsMuted` to `IPlaybackService`, implement via `MediaPlayer.IsMuted` in `PlaybackService`, simplify `PlayerViewModel.ToggleMute()`.
10. **Playback state**: Wire `PlayerViewModel` and `PlaybackService` to save/restore volume, mute, shuffle, repeat via `IAppStateService`.
11. **Window state**: Wire `ActivationService` (restore) and `MainWindow` (save on resize/move/close) to persist window geometry via `IAppStateService`.
12. **Navigation state**: Wire navigation service to save/restore last page.
13. **Cleanup**: Remove `"ServerCredential"` key handling from any remaining code paths.

## Verification

**Build:**
- `dotnet build Hypersonic.sln -p:Platform=x64` — must compile cleanly with zero Newtonsoft.Json references in the app project.
- Verify: `dotnet list Hypersonic/Hypersonic.csproj package` should not list Newtonsoft.Json.

**Existing tests (no regressions):**
- `dotnet test Hypersonic.Tests.MSTest`
- `dotnet test Hypersonic.Client.UnitTest`
- `dotnet test Hypersonic.UnitTest`

**New unit tests:**

*Pure logic tests* — location: `Hypersonic.UnitTest` (plain `net10.0`, no WinUI/WinRT dependencies). Source-link the vault key derivation helper and `AppState` model, test them in isolation:

Vault key derivation:
- `VaultKeyDerivation_FullUrl` — `https://music.example.com/rest` → `Hypersonic:https://music.example.com/rest`
- `VaultKeyDerivation_HttpWithPort` — `http://192.168.1.50:4533/` → `Hypersonic:http://192.168.1.50:4533/`
- `VaultKeyDerivation_DifferentSchemes_DifferentKeys` — `http://host` and `https://host` produce distinct vault keys
- `VaultKeyDerivation_PathBasedUrls_DifferentKeys` — `https://host/navidrome1` and `https://host/navidrome2` produce distinct vault keys

Serialization:
- `JsonHelper_SerializeDeserialize_RoundTrip` — System.Text.Json produces correct output
- `JsonHelper_ReadsLegacyNewtonsoftOutput` — can deserialize JSON written by old Newtonsoft code

App state:
- `AppState_Defaults` — freshly constructed `AppState` has correct default values
- `AppState_JsonRoundTrip` — serialize and deserialize preserves all fields

*Integration tests requiring WinRT* — location: `Hypersonic.Tests.MSTest` (targets `net10.0-windows10.0.19041.0`, has access to WinRT APIs):

Credential service (using real `PasswordVault`):
- `SaveCredential_AddsToServerAccounts` — saving a new credential adds to the accounts list
- `SaveCredential_UpdatesExistingAccount` — saving with same URL+username updates, doesn't duplicate
- `SetActiveServer_UpdatesActiveAccount` — sets `"ActiveAccount"` in settings as composite `ServerAccount`
- `GetActiveCredential_VaultMissing_ReturnsNull` — `"ActiveAccount"` set but vault entry deleted → returns null, does not throw
- `GetActiveCredential_ReconstructsFromVaultAndSettings` — returns full `ServerCredential`
- `GetActiveServerAccount_VaultMissing_StillReturnsAccount` — `"ActiveAccount"` set but vault entry deleted → returns `ServerAccount` with URL + username
- `MigrateLegacy_MovesPasswordToVault` — old `"ServerCredential"` key migrated, then deleted
- `MigrateLegacy_Idempotent` — running migration twice is safe

App state service (using real file I/O):
- `Initialize_NoFile_ReturnsDefaults` — missing `AppState.json` yields default `AppState`
- `Initialize_ExistingFile_Deserializes` — reads back previously saved state
- `SavePlayback_PersistsToFile` — volume/mute/shuffle/repeat round-trip
- `SaveWindow_PersistsToFile` — geometry round-trip
- `Initialize_CorruptFile_ReturnsDefaults` — malformed JSON yields defaults, logs warning

**Manual smoke test:**
1. Launch with existing `LocalSettings.json` containing `"ServerCredential"` → auto-migrates, connects normally, password now in Windows Credential Manager.
2. Relaunch → auto-connects via `PasswordVault` (no plain-text password in settings file).
3. Open Windows Credential Manager → verify `Hypersonic:<serverUrl>` entry visible.
4. Delete the vault entry manually in Credential Manager → relaunch → ConnectPage shows with URL and username prefilled, password field empty.
5. Change volume to 50%, enable shuffle, resize window, navigate to Albums page (top-level) → close app.
6. Relaunch → volume is 50%, shuffle is on, window restores to saved size/position, Albums page is shown.
7. Navigate to an album detail page → close app → relaunch → Albums page is shown (detail pages are not persisted, falls back to last top-level page).
8. Fresh install (no settings files) → defaults applied, connect page shown, everything works.
