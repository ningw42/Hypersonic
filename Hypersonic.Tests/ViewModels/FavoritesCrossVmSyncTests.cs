using CommunityToolkit.Mvvm.Messaging;

using Hypersonic.Models;
using Hypersonic.Services;
using Hypersonic.Tests.Fakes;
using Hypersonic.ViewModels;

namespace Hypersonic.Tests.ViewModels;

/// <summary>
/// Cross-surface favorite synchronization: a single FavoritesChangedMessage updates the
/// matching SongInfo instances owned by every registered VM, and the load-site
/// reconcile + overlay shows local truth while protecting an in-flight toggle.
/// </summary>
[TestClass]
public class FavoritesCrossVmSyncTests
{
    private static (FavoritesService favorites, WeakReferenceMessenger messenger, FakeSubsonicClientService clientService, RecordingSubsonicClient client) CreateFavorites()
    {
        var client = new RecordingSubsonicClient();
        var clientService = new FakeSubsonicClientService { Client = client };
        var messenger = new WeakReferenceMessenger();
        var favorites = new FavoritesService(clientService, new NullLoggingService(), messenger);
        return (favorites, messenger, clientService, client);
    }

    private static SongInfo Song(string id, bool starred = false) => new() { Id = id, IsStarred = starred };

    [TestMethod]
    public void Broadcast_UpdatesMatchingSongsAcrossAllRegisteredVms_LeavesOthersAlone()
    {
        var (favorites, messenger, clientService, _) = CreateFavorites();

        var vm1 = new TracksViewModel(clientService, new FakePlaybackService(), favorites, messenger, new NullLoggingService());
        var vm2 = new TracksViewModel(clientService, new FakePlaybackService(), favorites, messenger, new NullLoggingService());

        var s1InVm1 = Song("s1");
        var s2InVm1 = Song("s2");
        var s1InVm2 = Song("s1");   // same id, different instance/surface
        vm1._allSongs = new() { s1InVm1, s2InVm1 };
        vm2._allSongs = new() { s1InVm2 };

        messenger.Send(new Hypersonic.Messages.FavoritesChangedMessage(true, new[] { "s1" }));

        Assert.IsTrue(s1InVm1.IsStarred, "vm1's matching song updates");
        Assert.IsTrue(s1InVm2.IsStarred, "vm2's matching song updates (cross-VM sync)");
        Assert.IsFalse(s2InVm1.IsStarred, "a non-matching id is left alone");

        GC.KeepAlive(vm1);
        GC.KeepAlive(vm2);
    }

    [TestMethod]
    public async Task LoadSite_ReconcileAndOverlay_ShowsLocalTruth_AndDoesNotRegressTheToggle()
    {
        var (favorites, _, _, _) = CreateFavorites();

        // The load site captures its observation token BEFORE the fetch.
        var token = favorites.BeginObservation();

        // A local toggle lands during the fetch (after the token was captured).
        await favorites.ToggleAsync("s1", true);

        // The page then maps a SongInfo from the pre-toggle server response (s1 unstarred).
        var loaded = Song("s1", starred: false);

        // Load-site steps: reconcile the fresh observation through the gate, then overlay the store.
        favorites.Reconcile(new[] { (loaded.Id, loaded.IsStarred) }, token);
        loaded.IsStarred = favorites.IsStarred(loaded.Id);

        Assert.IsTrue(loaded.IsStarred, "Overlay replaces the stale server value with the locally-correct one");
        Assert.IsTrue(favorites.IsStarred("s1"), "Reconcile skipped the protected id — the toggle is not regressed");
    }
}
