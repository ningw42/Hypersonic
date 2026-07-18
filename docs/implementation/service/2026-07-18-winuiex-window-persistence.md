# WinUIEx-Backed Window Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make WinUIEx the sole owner of main-window placement semantics while keeping the opaque placement blob in the existing `AppState.json` document for both packaged and unpackaged builds.

**Architecture:** Add an app-layer `IDictionary<string, object>` adapter between WinUIEx's static `WindowManager.PersistenceStorage` hook and `IAppStateService`. Replace the five typed geometry fields with one nullable placement string, configure the adapter after app-state initialization and before activation, and remove Hypersonic's manual capture/restore pipeline while retaining tray behavior and close cleanup.

**Tech Stack:** .NET 10, C# 14, WinUI 3, Windows App SDK 2.3.1, WinUIEx 2.9.2, MSTest 4.3.2, System.Text.Json

## Global Constraints

- `PersistenceId` is exactly `"MainWindow"` and is defined once by `AppStateWindowPersistenceStorage`.
- The WinUIEx storage key is exactly `"WindowPersistance_" + PersistenceId`; WinUIEx's `Persistance` misspelling must remain verbatim.
- `AppState.Window.Placement` is the only application-owned representation of window placement and remains an opaque nullable string.
- Application code must never decode, validate, or modify the placement blob; WinUIEx owns `WINDOWPLACEMENT`, DPI, monitor-topology, and minimized/maximized semantics.
- `WindowManager.PersistenceStorage` must be assigned after `IAppStateService.InitializeAsync()` completes and before `App.MainWindow.Activate()`.
- The adapter depends only on `IAppStateService`, carries no logger, and holds no state of its own.
- `AppStateWindowPersistenceStorage` references no WinUIEx type, is not registered in DI, and is kept alive by the static `WindowManager.PersistenceStorage` reference.
- `AppStateService` and `Hypersonic.Core` must not reference WinUIEx.
- Only `ContainsKey` and the string indexer are supported; structural mutation and enumeration members throw `NotSupportedException`.
- Unknown-key and non-string indexer writes are silently ignored and never save.
- A valid string write updates `AppState.Window.Placement` and calls `SaveSync()` exactly once.
- Packaged and unpackaged builds must both use `AppStateService`; WinUIEx's default `ApplicationData.LocalSettings` storage must never be selected.
- Hide-to-tray paths do not flush placement. Only a real close saves placement, and only first activation in a new process restores it.
- No conversion, schema version, or migration marker is added for legacy `Width`, `Height`, `X`, `Y`, or `IsMaximized` JSON.
- Preserve `MinWidth="500"`, `MinHeight="500"`, minimize-to-tray, close-to-tray, explicit tray Exit, and all unrelated shutdown cleanup.

---

## File Structure

| File | Responsibility |
|---|---|
| `Hypersonic.Core/Models/AppState.cs` | Replace typed window geometry with the opaque nullable `Placement` value. |
| `Hypersonic.Core.Tests/Models/PersistenceTests.cs` | Verify placement defaults, JSON round-trip, and legacy-field tolerance. |
| `Hypersonic.Core/Contracts/Services/IAppStateService.cs` | Remove the obsolete window-specific async save member. |
| `Hypersonic/Services/AppStateService.cs` | Remove the obsolete `SaveWindowAsync()` wrapper while retaining full-document sync/async persistence. |
| `Hypersonic/Services/AppStateWindowPersistenceStorage.cs` | Implement the narrow WinUIEx property-bag adapter and own the shared persistence ID. |
| `Hypersonic.Tests/Fakes/FakeAppStateService.cs` | Provide an adapter-focused fake with an observable `SaveSyncCount`. |
| `Hypersonic.Tests/Services/AppStateWindowPersistenceStorageTests.cs` | Lock down the adapter's supported protocol and rejected operations. |
| `Hypersonic/Services/ActivationService.cs` | Install the adapter at the required startup boundary and remove manual restoration. |
| `Hypersonic/MainWindow.xaml` | Remove the stale manual-persistence comment while preserving minimum dimensions. |
| `Hypersonic/MainWindow.xaml.cs` | Assign the shared persistence ID and remove manual geometry capture/save code without changing tray or shutdown behavior. |
| Eight `Hypersonic.Tests/ViewModels/*Tests.cs` test-double files listed in Task 4 | Track the `IAppStateService` contract removal. |
| `window-persistence.md` | Convert the root decision brief into a resolved record pointing at the approved design. |

---

### Task 1: Introduce the Opaque Placement Field Without Breaking the Manual Path

**Files:**
- Modify: `Hypersonic.Core/Models/AppState.cs:19-26`
- Modify: `Hypersonic.Core.Tests/Models/PersistenceTests.cs:67-137`

**Interfaces:**
- Consumes: `Json.Options : JsonSerializerOptions` and the existing `AppState.Window : WindowState` property.
- Produces: `WindowState.Placement : string?`, consumed by `AppStateWindowPersistenceStorage` in Task 2. The five geometry properties remain temporarily so the app compiles until Task 3 removes their consumers.

- [ ] **Step 1: Extend the window-state assertions with placement-focused coverage**

In `Hypersonic.Core.Tests/Models/PersistenceTests.cs`, replace `AppState_Defaults` and `AppState_JsonRoundTrip` with the following complete methods. The legacy geometry assertions remain in the default test only to keep this preparatory commit compatible with the still-manual app path; Task 4 removes them and adds the migration test after the cutover.

```csharp
    [TestMethod]
    public void AppState_Defaults()
    {
        var state = new AppState();

        Assert.AreEqual(100, state.Playback.Volume);
        Assert.IsFalse(state.Playback.IsMuted);
        Assert.IsFalse(state.Playback.ShuffleEnabled);
        Assert.AreEqual(RepeatMode.Off, state.Playback.RepeatMode);

        Assert.IsNull(state.Window.Placement);
        Assert.IsNull(state.Window.Width);
        Assert.IsNull(state.Window.Height);
        Assert.IsNull(state.Window.X);
        Assert.IsNull(state.Window.Y);
        Assert.IsFalse(state.Window.IsMaximized);

        Assert.IsNull(state.Navigation.LastPage);

        Assert.IsFalse(state.General.MinimizeToTray);
        Assert.IsFalse(state.General.CloseToTray);
    }

    [TestMethod]
    public void AppState_JsonRoundTrip()
    {
        var state = new AppState
        {
            Playback = new PlaybackState
            {
                Volume = 75,
                IsMuted = true,
                ShuffleEnabled = true,
                RepeatMode = RepeatMode.All,
            },
            Window = new WindowState
            {
                Placement = "AQIDBAUG==",
            },
            Navigation = new NavigationState
            {
                LastPage = "Hypersonic.ViewModels.AlbumsViewModel",
            },
            General = new GeneralState
            {
                MinimizeToTray = true,
                CloseToTray = true,
            },
        };

        var json = JsonSerializer.Serialize(state, Json.Options);
        Assert.IsNotNull(json);
        Assert.IsTrue(json.Contains("\"RepeatMode\": \"All\""));

        var deserialized = JsonSerializer.Deserialize<AppState>(json, Json.Options);
        Assert.IsNotNull(deserialized);

        Assert.AreEqual(75, deserialized!.Playback.Volume);
        Assert.IsTrue(deserialized.Playback.IsMuted);
        Assert.IsTrue(deserialized.Playback.ShuffleEnabled);
        Assert.AreEqual(RepeatMode.All, deserialized.Playback.RepeatMode);

        Assert.AreEqual("AQIDBAUG==", deserialized.Window.Placement);

        Assert.AreEqual("Hypersonic.ViewModels.AlbumsViewModel", deserialized.Navigation.LastPage);
        Assert.IsTrue(deserialized.General.MinimizeToTray);
        Assert.IsTrue(deserialized.General.CloseToTray);
    }
```

- [ ] **Step 2: Run the focused Core tests and confirm the new contract fails**

Run:

```powershell
rtk dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~PersistenceTests" --nologo
```

Expected: FAIL to compile with `CS1061`/`CS0117` because `WindowState.Placement` does not exist yet.

- [ ] **Step 3: Add `Placement` alongside the temporarily retained geometry properties**

In `Hypersonic.Core/Models/AppState.cs`, add `Placement` as the first member of `WindowState`, producing this transitional class:

```csharp
public class WindowState
{
    public string? Placement { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool IsMaximized { get; set; }
}
```

This is an intentional transition seam: the manual path continues compiling while the adapter is built and wired. Task 4 removes these five fields immediately after Task 3 removes their last production consumers.

- [ ] **Step 4: Run the focused Core tests and confirm the migration behavior passes**

Run:

```powershell
rtk dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~PersistenceTests" --nologo
```

Expected: PASS, 9 tests. The serialized JSON contains `Placement`, and unrelated playback, navigation, and general state still round-trip.

- [ ] **Step 5: Build the solution to verify the additive seam**

Run:

```powershell
rtk dotnet build Hypersonic.sln -c Debug -p:Platform=x64 --nologo
```

Expected: Build succeeds because the temporary geometry members still satisfy the manual capture/restore code.

- [ ] **Step 6: Commit the model migration**

```powershell
rtk git add Hypersonic.Core/Models/AppState.cs Hypersonic.Core.Tests/Models/PersistenceTests.cs
rtk git commit -m "feat(window): add opaque placement to app state"
```

---

### Task 2: Add the AppState-Backed WinUIEx Persistence Adapter

**Files:**
- Create: `Hypersonic/Services/AppStateWindowPersistenceStorage.cs`
- Create: `Hypersonic.Tests/Fakes/FakeAppStateService.cs`
- Create: `Hypersonic.Tests/Services/AppStateWindowPersistenceStorageTests.cs`

**Interfaces:**
- Consumes: `IAppStateService.State.Window.Placement : string?` from Task 1 and `IAppStateService.SaveSync() : void`.
- Produces: `AppStateWindowPersistenceStorage : IDictionary<string, object>` and `AppStateWindowPersistenceStorage.PersistenceId : const string`, consumed by `ActivationService` and `MainWindow` in Task 3.

- [ ] **Step 1: Add the recording app-state fake and failing adapter tests**

Create `Hypersonic.Tests/Fakes/FakeAppStateService.cs` with:

```csharp
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Tests.Fakes;

public sealed class FakeAppStateService : IAppStateService
{
    public AppState State { get; } = new();
    public int SaveSyncCount { get; private set; }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task SaveAsync() => Task.CompletedTask;
    public Task SavePlaybackAsync() => Task.CompletedTask;
    public Task SaveWindowAsync() => Task.CompletedTask;
    public Task SaveNavigationAsync() => Task.CompletedTask;
    public Task SaveGeneralAsync() => Task.CompletedTask;

    public void SaveSync()
    {
        SaveSyncCount++;
    }
}
```

Create `Hypersonic.Tests/Services/AppStateWindowPersistenceStorageTests.cs` with:

```csharp
using System.Collections;

using Hypersonic.Services;
using Hypersonic.Tests.Fakes;

namespace Hypersonic.Tests.Services;

[TestClass]
public class AppStateWindowPersistenceStorageTests
{
    private const string PersistenceKey =
        "WindowPersistance_" + AppStateWindowPersistenceStorage.PersistenceId;

    [TestMethod]
    public void MissingPlacement_IsNotExposed()
    {
        var appState = new FakeAppStateService();
        var storage = new AppStateWindowPersistenceStorage(appState);

        Assert.IsFalse(storage.ContainsKey(PersistenceKey));
        Assert.ThrowsExactly<KeyNotFoundException>(() => _ = storage[PersistenceKey]);
    }

    [TestMethod]
    public void ExistingPlacement_IsExposedUnderWinUIExKey()
    {
        var appState = new FakeAppStateService();
        appState.State.Window.Placement = "AQIDBAUG==";
        var storage = new AppStateWindowPersistenceStorage(appState);

        Assert.IsTrue(storage.ContainsKey(PersistenceKey));
        Assert.AreEqual("AQIDBAUG==", storage[PersistenceKey]);
        Assert.IsFalse(storage.ContainsKey("MainWindow"));
    }

    [TestMethod]
    public void KnownStringWrite_UpdatesOnlyPlacementAndSavesOnce()
    {
        var appState = new FakeAppStateService();
        appState.State.Window.Placement = "old";
        appState.State.Playback.Volume = 27;
        appState.State.Navigation.LastPage = "Hypersonic.ViewModels.AlbumsViewModel";
        appState.State.General.CloseToTray = true;
        var storage = new AppStateWindowPersistenceStorage(appState);

        storage[PersistenceKey] = "new";

        Assert.AreEqual("new", appState.State.Window.Placement);
        Assert.AreEqual(27, appState.State.Playback.Volume);
        Assert.AreEqual("Hypersonic.ViewModels.AlbumsViewModel", appState.State.Navigation.LastPage);
        Assert.IsTrue(appState.State.General.CloseToTray);
        Assert.AreEqual(1, appState.SaveSyncCount);
    }

    [TestMethod]
    public void UnknownKeyWrite_IsIgnored()
    {
        var appState = new FakeAppStateService();
        appState.State.Window.Placement = "original";
        var storage = new AppStateWindowPersistenceStorage(appState);

        storage["WindowPersistance_OtherWindow"] = "replacement";

        Assert.AreEqual("original", appState.State.Window.Placement);
        Assert.AreEqual(0, appState.SaveSyncCount);
    }

    [TestMethod]
    public void NonStringWrite_IsIgnored()
    {
        var appState = new FakeAppStateService();
        appState.State.Window.Placement = "original";
        var storage = new AppStateWindowPersistenceStorage(appState);

        storage[PersistenceKey] = 42;

        Assert.AreEqual("original", appState.State.Window.Placement);
        Assert.AreEqual(0, appState.SaveSyncCount);
    }

    [TestMethod]
    public void StructuralAndEnumerationMembers_AreNotSupported()
    {
        var storage = new AppStateWindowPersistenceStorage(new FakeAppStateService());
        var pair = new KeyValuePair<string, object>(PersistenceKey, "value");
        var destination = new KeyValuePair<string, object>[1];

        Assert.ThrowsExactly<NotSupportedException>(() => _ = storage.Keys);
        Assert.ThrowsExactly<NotSupportedException>(() => _ = storage.Values);
        Assert.ThrowsExactly<NotSupportedException>(() => _ = storage.Count);
        Assert.ThrowsExactly<NotSupportedException>(() => _ = storage.IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => storage.Add(PersistenceKey, "value"));
        Assert.ThrowsExactly<NotSupportedException>(() => storage.Add(pair));
        Assert.ThrowsExactly<NotSupportedException>(() => _ = storage.Remove(PersistenceKey));
        Assert.ThrowsExactly<NotSupportedException>(() => _ = storage.Remove(pair));
        Assert.ThrowsExactly<NotSupportedException>(storage.Clear);
        Assert.ThrowsExactly<NotSupportedException>(() => _ = storage.Contains(pair));
        Assert.ThrowsExactly<NotSupportedException>(() => storage.TryGetValue(PersistenceKey, out _));
        Assert.ThrowsExactly<NotSupportedException>(() => storage.CopyTo(destination, 0));
        Assert.ThrowsExactly<NotSupportedException>(() => _ = storage.GetEnumerator());
        Assert.ThrowsExactly<NotSupportedException>(() => _ = ((IEnumerable)storage).GetEnumerator());
    }
}
```

- [ ] **Step 2: Run the adapter tests and confirm the production type is missing**

Run:

```powershell
rtk dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AppStateWindowPersistenceStorageTests" --nologo
```

Expected: FAIL to compile with `CS0246` because `AppStateWindowPersistenceStorage` has not been created.

- [ ] **Step 3: Implement the narrow dictionary adapter**

Create `Hypersonic/Services/AppStateWindowPersistenceStorage.cs` with:

```csharp
using System.Collections;

using Hypersonic.Core.Contracts.Services;

namespace Hypersonic.Services;

public sealed class AppStateWindowPersistenceStorage : IDictionary<string, object>
{
    public const string PersistenceId = "MainWindow";

    private const string PersistenceKey = "WindowPersistance_" + PersistenceId;
    private readonly IAppStateService _appStateService;

    public AppStateWindowPersistenceStorage(IAppStateService appStateService)
    {
        _appStateService = appStateService;
    }

    public object this[string key]
    {
        get
        {
            if (key == PersistenceKey &&
                _appStateService.State.Window.Placement is string placement)
            {
                return placement;
            }

            throw new KeyNotFoundException();
        }
        set
        {
            if (key != PersistenceKey || value is not string placement)
            {
                return;
            }

            _appStateService.State.Window.Placement = placement;
            _appStateService.SaveSync();
        }
    }

    public ICollection<string> Keys => throw new NotSupportedException();

    public ICollection<object> Values => throw new NotSupportedException();

    public int Count => throw new NotSupportedException();

    public bool IsReadOnly => throw new NotSupportedException();

    public bool ContainsKey(string key) =>
        key == PersistenceKey && _appStateService.State.Window.Placement is not null;

    public void Add(string key, object value) => throw new NotSupportedException();

    public bool Remove(string key) => throw new NotSupportedException();

    public bool TryGetValue(string key, out object value) => throw new NotSupportedException();

    public void Add(KeyValuePair<string, object> item) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(KeyValuePair<string, object> item) => throw new NotSupportedException();

    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) =>
        throw new NotSupportedException();

    public bool Remove(KeyValuePair<string, object> item) => throw new NotSupportedException();

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() =>
        throw new NotSupportedException();

    IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
}
```

The getter deliberately follows normal dictionary behavior for an absent entry. WinUIEx calls `ContainsKey` before reading, so a missing placement remains a no-restore path.

- [ ] **Step 4: Run the adapter tests and confirm the protocol passes**

Run:

```powershell
rtk dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AppStateWindowPersistenceStorageTests" --nologo
```

Expected: PASS, 6 tests.

- [ ] **Step 5: Commit the adapter and its tests**

```powershell
rtk git add Hypersonic/Services/AppStateWindowPersistenceStorage.cs Hypersonic.Tests/Fakes/FakeAppStateService.cs Hypersonic.Tests/Services/AppStateWindowPersistenceStorageTests.cs
rtk git commit -m "feat(window): bridge WinUIEx placement to app state"
```

---

### Task 3: Wire WinUIEx into Activation and Remove Manual Geometry Ownership

**Files:**
- Modify: `Hypersonic/Services/ActivationService.cs:1-9,42-59,173-220`
- Modify: `Hypersonic/MainWindow.xaml:1-16`
- Modify: `Hypersonic/MainWindow.xaml.cs:1-25,27-141,163-243`

**Interfaces:**
- Consumes: `AppStateWindowPersistenceStorage(IAppStateService)`, `AppStateWindowPersistenceStorage.PersistenceId`, and `WinUIEx.WindowManager.PersistenceStorage`.
- Produces: Startup order `InitializeAsync()` → assign `PersistenceStorage` → `Activate()`, plus a `MainWindow` that retains tray behavior but has no geometry capture/restore/save code.

- [ ] **Step 1: Configure persistence at the activation boundary and remove manual restoration**

In `Hypersonic/Services/ActivationService.cs`:

1. Remove these unused using directives:

```csharp
using Hypersonic.Core.Models;
using Microsoft.UI.Windowing;
```

2. Immediately after `await InitializeAsync();` in `ActivateAsync`, add:

```csharp
        // WinUIEx reads this lazily on the first WM_SHOWWINDOW. AppState must
        // already be loaded, and the adapter must be installed before Activate().
        WinUIEx.WindowManager.PersistenceStorage =
            new AppStateWindowPersistenceStorage(_appStateService);
```

3. Delete this call and its comment from `ActivateAsync`:

```csharp
        // Restore window geometry from saved state.
        RestoreWindowState();
```

4. Delete both obsolete methods in their entirety:

```csharp
    private void RestoreWindowState()
    {
        var windowState = _appStateService.State.Window;
        var window = App.MainWindow;

        // Always apply saved normal bounds first, so the window's restore-size
        // is correct regardless of whether we then maximize.
        if (windowState.Width.HasValue && windowState.Height.HasValue)
        {
            window.Width = windowState.Width.Value;
            window.Height = windowState.Height.Value;
        }

        if (windowState.X.HasValue && windowState.Y.HasValue)
        {
            if (IsPositionOnScreen(windowState.X.Value, windowState.Y.Value))
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(windowState.X.Value, windowState.Y.Value));
                _log.Debug("Window position restored", new { windowState.X, windowState.Y });
            }
            else
            {
                _log.Debug("Saved window position is off-screen, using OS default");
            }
        }

        if (windowState.IsMaximized)
        {
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
            _log.Debug("Window restored to maximized state");
        }
    }

    private static bool IsPositionOnScreen(int x, int y)
    {
        try
        {
            var areas = DisplayArea.FindAll();
            foreach (var area in areas)
            {
                var bounds = area.WorkArea;
                if (x >= bounds.X && x < bounds.X + bounds.Width &&
                    y >= bounds.Y && y < bounds.Y + bounds.Height)
                {
                    return true;
                }
            }
        }
        catch
        {
            // If we can't enumerate displays, allow the position.
            return true;
        }

        return false;
    }
```

- [ ] **Step 2: Remove the stale XAML ownership comment**

Replace `Hypersonic/MainWindow.xaml` with this complete content. Do not add `PersistenceId` in XAML; code-behind consumes the shared constant.

```xml
<windowex:WindowEx
    x:Class="Hypersonic.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:Hypersonic"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:windowex="using:WinUIEx"
    MinWidth="500"
    MinHeight="500"
    mc:Ignorable="d">
</windowex:WindowEx>
```

- [ ] **Step 3: Assign the shared ID and reduce `MainWindow` to tray/lifecycle responsibilities**

Apply the following exact changes in `Hypersonic/MainWindow.xaml.cs`:

```diff
 using Hypersonic.Core.Services;
 using Hypersonic.Helpers;
+using Hypersonic.Services;
 using Hypersonic.ViewModels;
@@
    private UISettings settings;

     private IAppStateService? _appStateService;
-    private DispatcherQueueTimer? _windowSaveTimer;
-    private bool _isClosing;
@@
     {
        InitializeComponent();
+        PersistenceId = AppStateWindowPersistenceStorage.PersistenceId;

         AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
@@
        settings.ColorValuesChanged += Settings_ColorValuesChanged; // cannot use FrameworkElement.ActualThemeChanged event

         Closed += MainWindow_Closed;
-        SizeChanged += MainWindow_SizeChanged;
         AppWindow.Changed += AppWindow_Changed;
         AppWindow.Closing += AppWindow_Closing;
@@
         try
         {
             _appStateService = App.GetService<IAppStateService>();
-            _windowSaveTimer = dispatcherQueue.CreateTimer();
-            _windowSaveTimer.Interval = TimeSpan.FromSeconds(1);
-            _windowSaveTimer.IsRepeating = false;
-            _windowSaveTimer.Tick += (_, _) => WriteWindowState(sync: false);
         }
         catch
@@
-    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
-    {
-        if (_isClosing)
-        {
-            return;
-        }
-
-        EnsureAppStateService();
-        CaptureWindowState();
-        DebounceSaveWindow();
-    }
-
     private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
     {
-        if (_isClosing)
-        {
-            return;
-        }
-
-        if (args.DidPositionChange || args.DidPresenterChange)
-        {
-            EnsureAppStateService();
-            CaptureWindowState();
-            DebounceSaveWindow();
-        }
-
         // Hide-to-tray when the user minimizes (only if the toggle is on).
         if (args.DidPresenterChange &&
             AppWindow.Presenter is OverlappedPresenter op &&
-            op.State == OverlappedPresenterState.Minimized &&
-            _appStateService?.State.General.MinimizeToTray == true)
+            op.State == OverlappedPresenterState.Minimized)
         {
-            // Flush synchronously before hiding. CaptureWindowState() above
-            // was a no-op for this transition (presenter is now Minimized so
-            // we skip overwriting the model with iconic geometry), but the
-            // model already holds the *pre-minimize* values from the last
-            // size/position change. Persist those now so a process kill
-            // while hidden doesn't lose them.
-            _windowSaveTimer?.Stop();
-            WriteWindowState(sync: true);
-
-            this.Hide(enableEfficiencyMode: false);
+            EnsureAppStateService();
+            if (_appStateService?.State.General.MinimizeToTray == true)
+            {
+                this.Hide(enableEfficiencyMode: false);
+            }
         }
     }
@@
     {
         // Real exit was requested from the tray menu - let it proceed.
-        if (App.IsExiting) return;
+        if (App.IsExiting)
+        {
+            return;
+        }

         EnsureAppStateService();
         if (_appStateService?.State.General.CloseToTray == true)
         {
             args.Cancel = true;
-
-            // Flush window state synchronously before hiding, mirroring the
-            // sync save in MainWindow_Closed. Without this, a resize or move
-            // made within ~1s of clicking X would only be persisted by the
-            // pending debounce timer firing after Hide() -- and that write
-            // can be lost if the process is terminated (task manager kill,
-            // OS shutdown, crash) before the user picks Exit-from-tray.
-            _windowSaveTimer?.Stop();
-            CaptureWindowState();
-            WriteWindowState(sync: true);
-
             this.Hide(enableEfficiencyMode: false);
         }
     }
@@
-    private void DebounceSaveWindow()
-    {
-        if (_isClosing || _appStateService == null || _windowSaveTimer == null)
-        {
-            return;
-        }
-
-        _windowSaveTimer.Stop();
-        _windowSaveTimer.Start();
-    }
-
-    /// <summary>
-    /// Updates the in-memory window-state model from the live window
-    /// properties. Skipped while the presenter is Minimized so iconic
-    /// bounds never clobber the user's real geometry. Cheap; safe to
-    /// call from every size/position event.
-    /// </summary>
-    private void CaptureWindowState()
-    {
-        if (_appStateService == null)
-        {
-            return;
-        }
-
-        if (AppWindow.Presenter is OverlappedPresenter mp &&
-            mp.State == OverlappedPresenterState.Minimized)
-        {
-            return;
-        }
-
-        var windowState = _appStateService.State.Window;
-        var isMaximized = AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Maximized;
-
-        windowState.IsMaximized = isMaximized;
-
-        if (!isMaximized)
-        {
-            windowState.Width = (int)Width;
-            windowState.Height = (int)Height;
-            var pos = AppWindow.Position;
-            windowState.X = pos.X;
-            windowState.Y = pos.Y;
-        }
-    }
-
-    /// <summary>
-    /// Persists whatever the window-state model currently holds. Does NOT
-    /// re-read the live window -- callers should call CaptureWindowState()
-    /// first if they want fresh values. Splitting capture from write lets
-    /// the timer-deferred path persist correctly even if the timer fires
-    /// after the window has already minimized: the model already holds
-    /// pre-minimize values, and writing them is the right behavior.
-    /// </summary>
-    private void WriteWindowState(bool sync)
-    {
-        if (_appStateService == null)
-        {
-            return;
-        }
-
-        if (sync)
-        {
-            _appStateService.SaveSync();
-        }
-        else
-        {
-            _ = _appStateService.SaveWindowAsync();
-        }
-    }
-
     private void MainWindow_Closed(object sender, WindowEventArgs args)
     {
-        _isClosing = true;
-        _windowSaveTimer?.Stop();
-
-        // Flush window state synchronously on close. Capture once first
-        // (no-op if minimized -- model retains good values from the last
-        // pre-minimize change).
-        CaptureWindowState();
-        WriteWindowState(sync: true);
-
         // Flush pending playback saves.
```

Leave everything after the playback flush in `MainWindow_Closed` byte-for-byte unchanged: taskbar disposal, playback disposal, image service/cache cleanup, tray disposal, logging disposal, and theme-change handling are unrelated shutdown responsibilities.

- [ ] **Step 4: Verify the structural ownership boundary**

Run:

```powershell
rtk rg -n "RestoreWindowState|IsPositionOnScreen|MainWindow_SizeChanged|CaptureWindowState|DebounceSaveWindow|WriteWindowState|_windowSaveTimer" Hypersonic Hypersonic.Core Hypersonic.Tests Hypersonic.Core.Tests --glob "*.cs"
```

Expected: no matches.

Run:

```powershell
rtk rg -n "PersistenceStorage|PersistenceId" Hypersonic/Services/AppStateWindowPersistenceStorage.cs Hypersonic/Services/ActivationService.cs Hypersonic/MainWindow.xaml.cs
```

Expected: the adapter defines `PersistenceId` and composes the key from it; activation assigns `WindowManager.PersistenceStorage`; `MainWindow` assigns its property from the shared constant.

- [ ] **Step 5: Build the WinUI app**

Run:

```powershell
rtk dotnet build Hypersonic/Hypersonic.csproj -c Debug -p:Platform=x64 --nologo
```

Expected: Build succeeds. In particular, XAML compilation succeeds, the adapter satisfies `WindowManager.PersistenceStorage`, and no removed manual geometry method remains referenced.

- [ ] **Step 6: Run the WinUI-dependent tests**

Run:

```powershell
rtk dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --nologo
```

Expected: PASS, including all 6 adapter tests and all existing tray/settings/player tests.

- [ ] **Step 7: Commit the lifecycle integration**

```powershell
rtk git add Hypersonic/Services/ActivationService.cs Hypersonic/MainWindow.xaml Hypersonic/MainWindow.xaml.cs
rtk git commit -m "feat(window): let WinUIEx own placement lifecycle"
```

---

### Task 4: Remove Legacy Geometry State and the Obsolete Save API

**Files:**
- Modify: `Hypersonic.Core/Models/AppState.cs:19-27`
- Modify: `Hypersonic.Core.Tests/Models/PersistenceTests.cs:67-137`
- Modify: `Hypersonic.Core/Contracts/Services/IAppStateService.cs:5-15`
- Modify: `Hypersonic/Services/AppStateService.cs:59-69`
- Modify: `Hypersonic.Tests/Fakes/FakeAppStateService.cs:7-25`
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelFavoriteTests.cs:66-75`
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelJumpToQueueIndexTests.cs:67-76`
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelMetadataTests.cs:55-64`
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelPositionTests.cs:62-71`
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelRefreshQueueTests.cs:63-72`
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelToggleTests.cs:63-80`
- Modify: `Hypersonic.Tests/ViewModels/SettingsViewModelTestDoubles.cs:10-27`
- Modify: `Hypersonic.Tests/ViewModels/ShellViewModelSearchPlayTests.cs:90-99`

**Interfaces:**
- Consumes: Task 3's cutover, which removed every production read/write of `Width`, `Height`, `X`, `Y`, `IsMaximized`, and `SaveWindowAsync()`.
- Produces: The final `WindowState` containing only `Placement : string?` and the final `IAppStateService` contract without `SaveWindowAsync()`.

- [ ] **Step 1: Add the failing legacy-JSON migration assertion and finalize the default test**

In `Hypersonic.Core.Tests/Models/PersistenceTests.cs`, replace `AppState_Defaults` with:

```csharp
    [TestMethod]
    public void AppState_Defaults()
    {
        var state = new AppState();

        Assert.AreEqual(100, state.Playback.Volume);
        Assert.IsFalse(state.Playback.IsMuted);
        Assert.IsFalse(state.Playback.ShuffleEnabled);
        Assert.AreEqual(RepeatMode.Off, state.Playback.RepeatMode);

        Assert.IsNull(state.Window.Placement);

        Assert.IsNull(state.Navigation.LastPage);

        Assert.IsFalse(state.General.MinimizeToTray);
        Assert.IsFalse(state.General.CloseToTray);
    }
```

Add this test immediately after `AppState_JsonRoundTrip`:

```csharp
    [TestMethod]
    public void AppState_LegacyWindowGeometry_IsIgnored()
    {
        var legacyJson = """
            {
              "Playback": {
                "Volume": 42,
                "IsMuted": true,
                "ShuffleEnabled": false,
                "RepeatMode": "One"
              },
              "Window": {
                "Width": 1200,
                "Height": 800,
                "X": 100,
                "Y": 200,
                "IsMaximized": true
              },
              "Navigation": {
                "LastPage": "Hypersonic.ViewModels.TracksViewModel"
              },
              "General": {
                "MinimizeToTray": true,
                "CloseToTray": false
              }
            }
            """;

        var deserialized = JsonSerializer.Deserialize<AppState>(legacyJson, Json.Options);

        Assert.IsNotNull(deserialized);
        Assert.IsNull(deserialized!.Window.Placement);
        Assert.AreEqual(42, deserialized.Playback.Volume);
        Assert.IsTrue(deserialized.Playback.IsMuted);
        Assert.AreEqual(RepeatMode.One, deserialized.Playback.RepeatMode);
        Assert.AreEqual("Hypersonic.ViewModels.TracksViewModel", deserialized.Navigation.LastPage);
        Assert.IsTrue(deserialized.General.MinimizeToTray);
        Assert.IsFalse(deserialized.General.CloseToTray);

        var migratedJson = JsonSerializer.Serialize(deserialized, Json.Options);
        Assert.IsFalse(migratedJson.Contains("\"Width\"", StringComparison.Ordinal));
        Assert.IsFalse(migratedJson.Contains("\"Height\"", StringComparison.Ordinal));
        Assert.IsFalse(migratedJson.Contains("\"X\"", StringComparison.Ordinal));
        Assert.IsFalse(migratedJson.Contains("\"Y\"", StringComparison.Ordinal));
        Assert.IsFalse(migratedJson.Contains("\"IsMaximized\"", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run the migration test and confirm the legacy properties are still serialized**

Run:

```powershell
rtk dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~PersistenceTests" --nologo
```

Expected: FAIL in `AppState_LegacyWindowGeometry_IsIgnored` because the transitional geometry properties still deserialize and reserialize.

- [ ] **Step 3: Reduce `WindowState` to the final opaque representation**

Replace the complete `WindowState` class in `Hypersonic.Core/Models/AppState.cs` with:

```csharp
public class WindowState
{
    public string? Placement { get; set; }
}
```

Do not add aliases, a schema marker, a migration converter, or application-side placement parsing.

- [ ] **Step 4: Remove `SaveWindowAsync()` from the production contract and implementation**

Replace `IAppStateService` with this complete interface:

```csharp
using Hypersonic.Core.Models;

namespace Hypersonic.Core.Contracts.Services;

public interface IAppStateService
{
    AppState State { get; }
    Task InitializeAsync();
    Task SaveAsync();
    Task SavePlaybackAsync();
    Task SaveNavigationAsync();
    Task SaveGeneralAsync();
    void SaveSync();
}
```

In `Hypersonic/Services/AppStateService.cs`, make the async wrapper block exactly:

```csharp
    public Task SaveAsync() => WriteStateAsync();

    public Task SavePlaybackAsync() => WriteStateAsync();

    public Task SaveNavigationAsync() => WriteStateAsync();

    public Task SaveGeneralAsync() => WriteStateAsync();
```

- [ ] **Step 5: Remove the obsolete member from all nine test doubles**

Delete this exact member from `Hypersonic.Tests/Fakes/FakeAppStateService.cs` and each of the eight ViewModel test files named above:

```csharp
public Task SaveWindowAsync() => Task.CompletedTask;
```

Keep `FakeAppStateService.SaveSyncCount` and every existing test-specific behavior unchanged.

- [ ] **Step 6: Verify all legacy symbols are gone**

Run:

```powershell
rtk rg -n "SaveWindowAsync|windowState\.(Width|Height|X|Y|IsMaximized)|State\.Window\.(Width|Height|X|Y|IsMaximized)" Hypersonic Hypersonic.Core Hypersonic.Tests Hypersonic.Core.Tests --glob "*.cs"
```

Expected: no matches.

- [ ] **Step 7: Run the final Core model tests**

Run:

```powershell
rtk dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~PersistenceTests" --nologo
```

Expected: PASS, 10 Core persistence tests.

- [ ] **Step 8: Run the final adapter tests**

Run:

```powershell
rtk dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AppStateWindowPersistenceStorageTests" --nologo
```

Expected: PASS, 6 adapter tests.

- [ ] **Step 9: Build the complete solution**

Run:

```powershell
rtk dotnet build Hypersonic.sln -c Debug -p:Platform=x64 --nologo
```

Expected: Build succeeds with no obsolete window-state member references.

- [ ] **Step 10: Run the complete WinUI test suite**

Run:

```powershell
rtk dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --nologo
```

Expected: PASS, all WinUI-dependent tests.

- [ ] **Step 11: Commit the legacy-state removal**

```powershell
rtk git add Hypersonic.Core/Models/AppState.cs Hypersonic.Core.Tests/Models/PersistenceTests.cs Hypersonic.Core/Contracts/Services/IAppStateService.cs Hypersonic/Services/AppStateService.cs Hypersonic.Tests/Fakes/FakeAppStateService.cs Hypersonic.Tests/ViewModels/PlayerViewModelFavoriteTests.cs Hypersonic.Tests/ViewModels/PlayerViewModelJumpToQueueIndexTests.cs Hypersonic.Tests/ViewModels/PlayerViewModelMetadataTests.cs Hypersonic.Tests/ViewModels/PlayerViewModelPositionTests.cs Hypersonic.Tests/ViewModels/PlayerViewModelRefreshQueueTests.cs Hypersonic.Tests/ViewModels/PlayerViewModelToggleTests.cs Hypersonic.Tests/ViewModels/SettingsViewModelTestDoubles.cs Hypersonic.Tests/ViewModels/ShellViewModelSearchPlayTests.cs
rtk git commit -m "refactor(window): remove manual geometry state"
```

---

### Task 5: Resolve the Decision Record and Complete End-to-End Verification

**Files:**
- Modify: `window-persistence.md:1-158`

**Interfaces:**
- Consumes: The completed model, adapter, activation order, and tray lifecycle from Tasks 1-4.
- Produces: A resolved root decision record and verification evidence for packaged and unpackaged builds.

- [ ] **Step 1: Replace the open decision brief with the resolved record**

Replace `window-persistence.md` with:

```markdown
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
```

- [ ] **Step 2: Run all Core tests**

Run:

```powershell
rtk dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --nologo
```

Expected: PASS, including the 10 `PersistenceTests` cases.

- [ ] **Step 3: Run all WinUI-dependent tests**

Run:

```powershell
rtk dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --nologo
```

Expected: PASS, including the 6 adapter cases.

- [ ] **Step 4: Build the complete solution**

Run:

```powershell
rtk dotnet build Hypersonic.sln -c Debug -p:Platform=x64 --nologo
```

Expected: Build succeeds with zero errors.

- [ ] **Step 5: Validate normal, maximized, and minimized restore semantics in both packaging modes**

For an MSIX deployment (Visual Studio F5/deploy) and an unpackaged launch, perform these exact checks:

1. Place and resize the window normally, close it, relaunch, and verify size and position return.
2. Maximize, close, relaunch, verify it is maximized, then restore and verify the previous normal bounds return.
3. Minimize from normal, choose tray **Exit**, relaunch, and verify it opens normal rather than minimized.
4. Maximize, minimize, choose tray **Exit**, relaunch, and verify it opens maximized rather than minimized.

Expected: all four checks pass in both modes, and each mode writes `Window.Placement` inside its package-appropriate `AppState.json`.

- [ ] **Step 6: Validate same-monitor scaling behavior in both directions**

For both packaging modes:

1. On a 4K monitor at 200% scaling, save a recognizable physical rectangle, switch that monitor to 100%, relaunch, and verify the same physical portion is retained.
2. Save at 100%, switch to 200%, relaunch, and verify the same physical portion is retained.
3. At 100%, shrink to the 500-DIP minimum, close, switch to 200%, and relaunch.

Expected: physical coverage is preserved. The minimum-sized case may reopen marginally below the current DIP minimum until resized; this is accepted.

- [ ] **Step 7: Validate mixed-DPI and changed-topology safety**

For both packaging modes:

1. Move the window between monitors with different DPI values, close on the secondary monitor, and verify relaunch returns it to that saved monitor and rectangle.
2. Save placement, then separately remove/add a monitor, rearrange monitors, rotate a monitor, and change its resolution before relaunching.

Expected: unchanged topology restores the saved placement. Every changed-topology case rejects the blob and leaves a visible Windows-chosen safe default.

- [ ] **Step 8: Validate tray behavior remains in-session and asymmetric**

For both packaging modes:

1. Enable minimize-to-tray, minimize, reopen from the tray, and verify the live window returns to its pre-hide size and position.
2. Enable close-to-tray, click **X**, reopen from the tray, and verify the live window returns unchanged.
3. After either hidden state, choose tray **Exit**, relaunch, and verify the final real close produced the next launch's placement.

Expected: show/hide does not consult disk, does not create a new placement write by itself, and explicit Exit still persists placement.

- [ ] **Step 9: Validate missing and corrupt placement fallback**

For both packaging modes, close the app, first remove `Window.Placement` from `AppState.json`, then repeat with a non-Base64 string, launching after each edit.

Expected: both launches succeed with a visible safe default. The app neither throws during startup nor attempts application-side blob repair.

- [ ] **Step 10: Commit the resolved record**

```powershell
rtk git add window-persistence.md
rtk git commit -m "docs(window): resolve persistence ownership"
```

---

## Self-Review

**Spec coverage:**

- The opaque field, null default, and placement round-trip are introduced compatibly in Task 1.
- Known-key reads/writes, exactly one sync save, ignored writes, unsupported dictionary operations, shared ID, and key spelling are covered by Task 2.
- Required activation order, code-behind `PersistenceId`, packaged/unpackaged custom storage, removal of manual restoration/capture/debounce/geometry flushes, and preservation of tray/shutdown behavior are covered by Task 3.
- Final removal of the five geometry properties, legacy-field migration, and removal of `SaveWindowAsync()` from production plus every current test double are covered by Task 4.
- Resolution of the root decision brief, full automated verification, and every item in the manual validation matrix are covered by Task 5.
- Non-goals remain absent: no snap-group, virtual-desktop, fullscreen, compact-overlay, cross-package sharing, readable blob, or settings-service consolidation work is introduced.

**Type consistency:**

- `WindowState.Placement : string?` is introduced in Task 1, read and written by Task 2, and retained as the sole final window member in Task 4.
- `AppStateWindowPersistenceStorage.PersistenceId : const string` is defined in Task 2 and consumed verbatim by `MainWindow` in Task 3.
- `AppStateWindowPersistenceStorage(IAppStateService)` is defined in Task 2 and constructed with `_appStateService` by `ActivationService` in Task 3.
- The final `IAppStateService` contract from Task 4 exactly matches the Task 2 fake after its obsolete member is removed, and retains the `SaveSync()` member the adapter invokes.
- The adapter's compile-time `PersistenceKey` and the tests' expected key both concatenate `"WindowPersistance_"` with the same shared `PersistenceId`.
