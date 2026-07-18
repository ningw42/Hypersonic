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
