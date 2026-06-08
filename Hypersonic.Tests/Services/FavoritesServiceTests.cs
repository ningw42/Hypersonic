using CommunityToolkit.Mvvm.Messaging;

using Hypersonic.Contracts.Services;
using Hypersonic.Messages;
using Hypersonic.Services;
using Hypersonic.Tests.Fakes;

namespace Hypersonic.Tests.Services;

[TestClass]
public class FavoritesServiceTests
{
    // Keeps a strong reference to a weak-registered recipient and records every broadcast.
    private sealed class Recorder
    {
        public readonly List<FavoritesChangedMessage> Messages = new();

        public Recorder(IMessenger messenger)
            => messenger.Register<FavoritesChangedMessage>(this, (_, m) => Messages.Add(m));

        public IReadOnlyList<string> Added => Messages.Where(m => m.IsStarred).SelectMany(m => m.Ids).ToList();
        public IReadOnlyList<string> Removed => Messages.Where(m => !m.IsStarred).SelectMany(m => m.Ids).ToList();
    }

    private static (FavoritesService svc, RecordingSubsonicClient client, FakeSubsonicClientService clientService, WeakReferenceMessenger messenger, Recorder recorder) Create()
    {
        var client = new RecordingSubsonicClient();
        var clientService = new FakeSubsonicClientService { Client = client };
        var messenger = new WeakReferenceMessenger();
        var svc = new FavoritesService(clientService, new NullLoggingService(), messenger);
        var recorder = new Recorder(messenger);
        return (svc, client, clientService, messenger, recorder);
    }

    // ---- Toggle basics ----

    [TestMethod]
    public async Task Toggle_Star_CallsStarUpdatesStoreAndBroadcasts()
    {
        var (svc, client, _, _, recorder) = Create();

        await svc.ToggleAsync("s1", true);

        Assert.AreEqual(1, client.StarCallCount);
        Assert.AreEqual("s1", client.LastStarredId);
        Assert.IsTrue(svc.IsStarred("s1"));
        Assert.AreEqual(1, recorder.Messages.Count);
        Assert.IsTrue(recorder.Messages[0].IsStarred);
        CollectionAssert.AreEqual(new[] { "s1" }, recorder.Messages[0].Ids.ToArray());
    }

    [TestMethod]
    public async Task Toggle_Unstar_CallsUnstarClearsStore()
    {
        var (svc, client, _, _, _) = Create();
        await svc.ToggleAsync("s1", true);

        await svc.ToggleAsync("s1", false);

        Assert.AreEqual(1, client.StarCallCount);
        Assert.AreEqual(1, client.UnstarCallCount);
        Assert.IsFalse(svc.IsStarred("s1"));
    }

    [TestMethod]
    public async Task Toggle_Throws_RevertsStoreAndBroadcastsRevert()
    {
        var (svc, client, _, _, recorder) = Create();
        client.StarShouldThrow = true;

        await svc.ToggleAsync("s1", true);

        Assert.IsFalse(svc.IsStarred("s1"), "Store reverts to the confirmed server value");
        Assert.AreEqual(2, recorder.Messages.Count, "Optimistic broadcast then revert broadcast");
        Assert.IsTrue(recorder.Messages[0].IsStarred);
        Assert.IsFalse(recorder.Messages[1].IsStarred);
    }

    [TestMethod]
    public async Task Toggle_FailedStatus_RevertsStore()
    {
        var (svc, client, _, _, _) = Create();
        client.ResponseStatus = "failed";

        await svc.ToggleAsync("s1", true);

        Assert.IsFalse(svc.IsStarred("s1"), "status==failed reverts even though no exception was thrown");
    }

    [TestMethod]
    public async Task Toggle_NullClient_NoOpNoBroadcast()
    {
        var (svc, _, clientService, _, recorder) = Create();
        clientService.Client = null;

        await svc.ToggleAsync("s1", true);

        Assert.IsFalse(svc.IsStarred("s1"));
        Assert.AreEqual(0, recorder.Messages.Count, "No optimistic flicker when disconnected");
    }

    // ---- Coalescing / serialization ----

    [TestMethod]
    public async Task ConcurrentSameId_Coalesced_OneOutstandingCallAtATime()
    {
        var (svc, client, _, _, _) = Create();
        client.Gate = new TaskCompletionSource<bool>();

        var t1 = svc.ToggleAsync("s1", true);   // becomes worker; StarAsync awaits the gate
        var t2 = svc.ToggleAsync("s1", false);  // records latest intent, returns after optimistic apply
        await t2;

        Assert.IsFalse(svc.IsStarred("s1"), "Latest intent applied optimistically");
        Assert.AreEqual(0, client.StarCallCount, "Star still in flight (gated)");

        client.Gate.SetResult(true);            // Star completes ok, then worker issues Unstar
        await t1;

        Assert.AreEqual(1, client.StarCallCount, "Exactly one Star issued");
        Assert.AreEqual(1, client.UnstarCallCount, "Exactly one Unstar issued, after Star confirmed");
        Assert.IsFalse(svc.IsStarred("s1"), "Converged to the latest intent");
    }

    [TestMethod]
    public async Task SupersededFailedCall_SettlesToConfirmed_NoSpuriousOppositeCall()
    {
        var (svc, client, _, _, _) = Create();
        client.Gate = new TaskCompletionSource<bool>();
        client.StarShouldThrow = true;

        var t1 = svc.ToggleAsync("s1", true);   // optimistic true; StarAsync (gated) will throw
        var t2 = svc.ToggleAsync("s1", false);  // latest intent back to server state (false)
        await t2;

        client.Gate.SetResult(true);            // StarAsync throws
        await t1;

        Assert.AreEqual(0, client.StarCallCount, "Star threw — not a success");
        Assert.AreEqual(0, client.UnstarCallCount, "Latest intent already matches server; no needless Unstar");
        Assert.IsFalse(svc.IsStarred("s1"), "Settled to confirmed false; no flip back to true");
    }

    // ---- Write gate: seq ordering ----

    [TestMethod]
    public async Task StaleSnapshot_RefreshDoesNotRegressCommittedToggle_LaterRefreshDoes()
    {
        var (svc, client, _, _, _) = Create();
        svc.Start();                                  // epoch 1, seed empty (floor set)

        // A refresh captures its obsSeq, then blocks in getStarred2 (server still has no s1).
        client.GetStarred2Gate = new TaskCompletionSource<bool>();
        var staleRefresh = svc.RefreshAsync();

        // A toggle commits s1 = starred AFTER the refresh captured its obsSeq.
        await svc.ToggleAsync("s1", true);
        Assert.IsTrue(svc.IsStarred("s1"));

        client.GetStarred2Gate.SetResult(true);       // stale snapshot (no s1) lands
        await staleRefresh;
        Assert.IsTrue(svc.IsStarred("s1"), "Committed toggle is newer than the stale snapshot — not regressed");

        // A fresh refresh (higher obsSeq) sees s1 still absent on the server → genuine external unstar applies.
        client.GetStarred2Gate = null;
        await svc.RefreshAsync();
        Assert.IsFalse(svc.IsStarred("s1"), "A newer read applies the genuine external change");
    }

    [TestMethod]
    public void ServerVsServer_OlderObservationRejected_NewerApplies()
    {
        var (svc, client, _, _, _) = Create();
        svc.Start();                                  // epoch 1

        var oldToken = svc.BeginObservation();        // captured before the refresh below

        client.StarredSongIds.Add("s1");
        svc.RefreshAsync().GetAwaiter().GetResult();  // newer read: s1 starred
        Assert.IsTrue(svc.IsStarred("s1"));

        svc.Reconcile(new[] { ("s1", false) }, oldToken);
        Assert.IsTrue(svc.IsStarred("s1"), "Older observation cannot overwrite the newer refresh");

        var newToken = svc.BeginObservation();        // captured after the refresh
        svc.Reconcile(new[] { ("s1", false) }, newToken);
        Assert.IsFalse(svc.IsStarred("s1"), "A later observation applies");
    }

    [TestMethod]
    public void Reconcile_NoDiffConfirmation_StampsAndRejectsOlderPartialRead()
    {
        var (svc, client, _, _, _) = Create();
        client.StarredSongIds.Add("s1");
        svc.Start();                                  // store: s1 starred

        var oldToken = svc.BeginObservation();        // page1 captured early; will claim s1 false

        var newToken = svc.BeginObservation();        // page2 captured later; confirms s1 true (no diff)
        svc.Reconcile(new[] { ("s1", true) }, newToken);

        svc.Reconcile(new[] { ("s1", false) }, oldToken);
        Assert.IsTrue(svc.IsStarred("s1"), "No-diff confirmation stamps the id so the older partial read is rejected");
    }

    [TestMethod]
    public void RefreshAbsenceFloor_RejectsOlderPageClaimingStarred()
    {
        var (svc, _, _, _, _) = Create();
        svc.Start();                                  // epoch 1, seed empty

        var oldToken = svc.BeginObservation();        // page captured before the refresh; claims s1 starred

        svc.RefreshAsync().GetAwaiter().GetResult();  // full snapshot: s1 absent → raises the absence floor

        svc.Reconcile(new[] { ("s1", true) }, oldToken);
        Assert.IsFalse(svc.IsStarred("s1"), "Full-refresh absence floor rejects an older page that claims s1 starred");
    }

    // ---- Refresh single-flight ----

    [TestMethod]
    public async Task RefreshSingleFlight_CoalescesAndRunsExactlyOneTrailingPass()
    {
        var (svc, client, _, _, _) = Create();
        var gate = new TaskCompletionSource<bool>();
        client.GetStarred2Gate = gate;

        var t1 = svc.RefreshAsync();                  // in flight, awaiting the gate
        var t2 = svc.RefreshAsync();                  // mid-flight request → sets Queued
        Assert.AreSame(t1, t2);

        client.GetStarred2Gate = null;                // trailing pass (if any) won't block
        gate.SetResult(true);                         // release the first pass
        await t1;

        Assert.AreEqual(2, client.GetStarred2CallCount, "One initial pass + exactly one trailing pass (not three)");
    }

    // ---- Epoch guard ----

    [TestMethod]
    public async Task Toggle_EpochChangesDuringCall_DiscardsResultNoBroadcast()
    {
        var (svc, client, _, _, recorder) = Create();
        client.Gate = new TaskCompletionSource<bool>();
        client.StarShouldThrow = true;

        var toggle = svc.ToggleAsync("s1", true);     // epoch 1: optimistic + worker awaits gate
        svc.Start();                                  // epoch 2: resets store, seeds empty
        recorder.Messages.Clear();                    // ignore everything up to and including the reset

        client.Gate.SetResult(true);                  // old worker resumes under epoch 2
        await toggle;

        Assert.IsFalse(svc.IsStarred("s1"));
        Assert.AreEqual(0, recorder.Messages.Count, "Replaced-connection worker writes/broadcasts nothing");
    }

    [TestMethod]
    public void Reconcile_StaleEpochToken_Ignored()
    {
        var (svc, _, _, _, recorder) = Create();
        svc.Start();                                  // epoch 1
        var token = svc.BeginObservation();           // epoch 1 token
        svc.Start();                                  // epoch 2
        recorder.Messages.Clear();

        svc.Reconcile(new[] { ("s1", true) }, token);

        Assert.IsFalse(svc.IsStarred("s1"), "Stale-epoch observation ignored wholesale");
        Assert.AreEqual(0, recorder.Messages.Count);
    }

    [TestMethod]
    public async Task NewEpochSeedsImmediately_OldRefreshFinallyInert()
    {
        var (svc, client, _, _, _) = Create();
        svc.Start();                                  // epoch 1, seed empty (no gate) → call #1

        // An epoch-1 refresh is left in flight, with a second call setting its lease's Queued.
        var gateOld = new TaskCompletionSource<bool>();
        client.GetStarred2Gate = gateOld;
        var oldRefresh = svc.RefreshAsync();          // epoch 1, suspended on gateOld
        _ = svc.RefreshAsync();                       // epoch 1, sets the old lease's Queued

        // A new epoch starts and seeds immediately (its seed is ungated → runs synchronously),
        // even while the old refresh is still in flight.
        client.GetStarred2Gate = null;
        client.StarredSongIds.Add("new");
        svc.Start();                                  // epoch 2 seed → call #2, applies {new}

        Assert.IsTrue(svc.IsStarred("new"), "New epoch seeds immediately, not blocked by the old refresh");

        // The old refresh now completes; under epoch 2 it is discarded and its finally is inert —
        // it neither clobbers the new store, clears the live lease, nor runs a trailing pass.
        gateOld.SetResult(true);                      // old refresh resumes → call #3, then discards
        await oldRefresh;

        Assert.IsTrue(svc.IsStarred("new"), "Replaced-connection refresh does not clobber the new store");
        Assert.AreEqual(3, client.GetStarred2CallCount,
            "epoch1 seed + epoch2 seed + old refresh; the stale Queued starts no extra trailing pass");
    }

    // ---- Refresh diff ----

    [TestMethod]
    public async Task RefreshDiff_AddsAndRemoves_TwoBatchedDeltas()
    {
        var (svc, client, _, _, recorder) = Create();
        client.StarredSongIds.Add("s1");
        client.StarredSongIds.Add("s2");
        svc.Start();                                  // store: s1, s2
        recorder.Messages.Clear();

        client.StarredSongIds.Remove("s1");
        client.StarredSongIds.Add("s3");              // server now: s2, s3
        await svc.RefreshAsync();

        Assert.IsTrue(svc.IsStarred("s2"));
        Assert.IsTrue(svc.IsStarred("s3"));
        Assert.IsFalse(svc.IsStarred("s1"));
        CollectionAssert.AreEquivalent(new[] { "s3" }, recorder.Added.ToArray());
        CollectionAssert.AreEquivalent(new[] { "s1" }, recorder.Removed.ToArray());
    }

    [TestMethod]
    public async Task Refresh_FailedStatus_StoreUntouched()
    {
        var (svc, client, _, _, _) = Create();
        client.StarredSongIds.Add("s1");
        svc.Start();                                  // store: s1

        client.GetStarred2Status = "failed";
        client.StarredSongIds.Clear();
        await svc.RefreshAsync();

        Assert.IsTrue(svc.IsStarred("s1"), "A failed getStarred2 leaves the store untouched");
    }

    [TestMethod]
    public async Task Refresh_Throws_StoreUntouched()
    {
        var (svc, client, _, _, _) = Create();
        client.StarredSongIds.Add("s1");
        svc.Start();

        client.GetStarred2ShouldThrow = true;
        await svc.RefreshAsync();

        Assert.IsTrue(svc.IsStarred("s1"), "An unsupported/failed endpoint leaves the store untouched");
    }

    // ---- Reconcile ----

    [TestMethod]
    public void Reconcile_Differ_UpdatesAndBroadcasts_EqualSilent()
    {
        var (svc, _, _, _, recorder) = Create();
        svc.Start();
        recorder.Messages.Clear();

        svc.Reconcile(new[] { ("s1", true) }, svc.BeginObservation());
        Assert.IsTrue(svc.IsStarred("s1"));
        Assert.AreEqual(1, recorder.Messages.Count);

        recorder.Messages.Clear();
        svc.Reconcile(new[] { ("s1", true) }, svc.BeginObservation());
        Assert.AreEqual(0, recorder.Messages.Count, "An equal observation is silent");
    }

    [TestMethod]
    public async Task Reconcile_LeasedId_Skipped()
    {
        var (svc, client, _, _, _) = Create();
        client.Gate = new TaskCompletionSource<bool>();

        var toggle = svc.ToggleAsync("s1", true);     // s1 leased (worker in flight)
        svc.Reconcile(new[] { ("s1", false) }, svc.BeginObservation());

        Assert.IsTrue(svc.IsStarred("s1"), "A reconcile cannot clobber an in-flight toggle's id");

        client.Gate.SetResult(true);
        await toggle;
    }

    // ---- Start ----

    [TestMethod]
    public async Task Start_Idempotent_ResetsStoreAndLeaseAndReseeds()
    {
        var (svc, client, _, _, _) = Create();
        await svc.ToggleAsync("s1", true);            // store: s1
        Assert.IsTrue(svc.IsStarred("s1"));

        client.StarredSongIds.Clear();                // server has nothing
        svc.Start();                                  // epoch advances, store reset, seed empty

        Assert.IsFalse(svc.IsStarred("s1"), "Start clears the store and reseeds from the (empty) server");
    }

    [TestMethod]
    public async Task Start_SeedsFromServer()
    {
        var (svc, client, _, _, _) = Create();
        client.StarredSongIds.Add("a");
        client.StarredSongIds.Add("b");

        svc.Start();

        Assert.IsTrue(svc.IsStarred("a"));
        Assert.IsTrue(svc.IsStarred("b"));
        Assert.IsFalse(svc.IsStarred("c"));
        await Task.CompletedTask;
    }

    // ---- Pure diff helper ----

    [TestMethod]
    public void ComputeDiff_AddsRemovesAndHonorsSkip()
    {
        var server = new HashSet<string> { "keep", "add", "leased" };
        var current = new HashSet<string> { "keep", "remove", "leased" };

        var (added, removed, survivors) = FavoritesService.ComputeDiff(
            server, current, skip: id => id == "leased");

        CollectionAssert.AreEquivalent(new[] { "add" }, added.ToArray());
        CollectionAssert.AreEquivalent(new[] { "remove" }, removed.ToArray());
        CollectionAssert.DoesNotContain(survivors.ToArray(), "leased");
        CollectionAssert.Contains(survivors.ToArray(), "keep");
    }
}
