using Hypersonic.Helpers;
using Hypersonic.Views;

namespace Hypersonic.Tests.Helpers;

[TestClass]
public class NowPlayingBackStackPolicyTests
{
    // Helpers to keep test bodies readable.
    private static NavEntry Entry(Type t, object? p = null) => new(t, p);

    [TestMethod]
    public void OnNowPlayingItself_NoOp()
    {
        // While the user is on NowPlayingPage we do NOT prune — the page is
        // legitimately on top of the stack right now.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(NowPlayingPage)),
            backStack: new[] { Entry(typeof(MainPage)) });
        Assert.AreEqual(0, indices.Count);
    }

    [TestMethod]
    public void EmptyBackStack_NoOp()
    {
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(MainPage)),
            backStack: Array.Empty<NavEntry>());
        Assert.AreEqual(0, indices.Count);
    }

    [TestMethod]
    public void NoNowPlayingInBackStack_NoOp()
    {
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(AlbumsPage)),
            backStack: new[] { Entry(typeof(MainPage)), Entry(typeof(ArtistsPage)) });
        Assert.AreEqual(0, indices.Count);
    }

    [TestMethod]
    public void NowPlayingOnTopOfBackStack_Pruned()
    {
        // Typical case: user opened NowPlaying from MainPage, then clicked a
        // sidebar item. Back-stack now has MainPage, NowPlayingPage; current
        // is AlbumsPage. NowPlayingPage must be removed so Back goes to Main.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(AlbumsPage)),
            backStack: new[] { Entry(typeof(MainPage)), Entry(typeof(NowPlayingPage)) });
        CollectionAssert.AreEqual(new[] { 1 }, indices.ToArray());
    }

    [TestMethod]
    public void NowPlayingDeepInBackStack_StillPruned()
    {
        // Defensive: even if a prior bug or future change leaves a stale
        // NowPlayingPage somewhere lower, we strip it. The policy is
        // idempotent — running it on every navigation keeps the stack clean.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(AlbumsPage)),
            backStack: new[]
            {
                Entry(typeof(MainPage)),
                Entry(typeof(NowPlayingPage)),
                Entry(typeof(ArtistsPage)),
                Entry(typeof(NowPlayingPage)),
            });
        CollectionAssert.AreEqual(new[] { 1, 3 }, indices.ToArray());
    }

    [TestMethod]
    public void RoundTripThroughNowPlaying_RemovesSelfDuplicate()
    {
        // The bug we're guarding against: user starts on Main, opens
        // NowPlaying (back stack = [Main]), clicks Home/back to Main. Frame
        // pushes NowPlaying, so back stack at handler entry = [Main, NowPlaying].
        // We strip NowPlaying AND the now-top-of-stack Main that duplicates the
        // visible page. Otherwise Back would re-navigate to the same page.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(MainPage)),
            backStack: new[] { Entry(typeof(MainPage)), Entry(typeof(NowPlayingPage)) });
        CollectionAssert.AreEqual(new[] { 0, 1 }, indices.ToArray());
    }

    [TestMethod]
    public void TopDuplicateWithoutNowPlaying_RemovesIt()
    {
        // Same self-duplicate guard, no NowPlaying involved. Any path that
        // lands on a page already at the top of the back stack is a redundant
        // entry that Back would just revisit.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(AlbumsPage)),
            backStack: new[] { Entry(typeof(MainPage)), Entry(typeof(AlbumsPage)) });
        CollectionAssert.AreEqual(new[] { 1 }, indices.ToArray());
    }

    [TestMethod]
    public void DeeperDuplicate_Preserved()
    {
        // [A, B, A] with current = A — only the topmost A is a self-duplicate.
        // The deeper A is real history (e.g. user navigated A → B → A → B → A).
        // Removing it would erase that history.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(MainPage)),
            backStack: new[] { Entry(typeof(MainPage)), Entry(typeof(AlbumsPage)), Entry(typeof(MainPage)) });
        CollectionAssert.AreEqual(new[] { 2 }, indices.ToArray());
    }

    [TestMethod]
    public void ContiguousTopRunOfDuplicates_AllRemoved()
    {
        // Defensive: if multiple Main entries somehow stacked at the top
        // (older bug, repeated nav), collapse the whole contiguous run.
        // Stops at the first non-match below.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(MainPage)),
            backStack: new[]
            {
                Entry(typeof(AlbumsPage)),
                Entry(typeof(MainPage)),
                Entry(typeof(MainPage)),
            });
        CollectionAssert.AreEqual(new[] { 1, 2 }, indices.ToArray());
    }

    [TestMethod]
    public void NowPlayingBetweenSelfAndDuplicate_BothRemoved()
    {
        // [Main, NowPlaying] with current = Main. Walking down from the top:
        // skip NowPlaying (already pruned), then see Main matching current —
        // remove it too. Same logical case as RoundTripThroughNowPlaying but
        // expressed as the canonical "skip pruned then check" path.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(MainPage)),
            backStack: new[]
            {
                Entry(typeof(AlbumsPage)),
                Entry(typeof(MainPage)),
                Entry(typeof(NowPlayingPage)),
            });
        CollectionAssert.AreEqual(new[] { 1, 2 }, indices.ToArray());
    }

    // ── Parameter-aware cases ─────────────────────────────────────────────

    [TestMethod]
    public void DetailPage_DifferentParameters_NotPruned()
    {
        // AlbumDetail(A) is in the back stack; current is AlbumDetail(B).
        // Different albums — Back should still go to A. Type-only comparison
        // would have wrongly collapsed these into one.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(AlbumDetailPage), "album-b"),
            backStack: new[]
            {
                Entry(typeof(MainPage)),
                Entry(typeof(AlbumDetailPage), "album-a"),
            });
        Assert.AreEqual(0, indices.Count);
    }

    [TestMethod]
    public void DetailPage_SameParameter_Pruned()
    {
        // Same album navigated to twice in a row (e.g. via a different entry
        // point). The top entry is genuinely a self-duplicate.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(AlbumDetailPage), "album-a"),
            backStack: new[]
            {
                Entry(typeof(MainPage)),
                Entry(typeof(AlbumDetailPage), "album-a"),
            });
        CollectionAssert.AreEqual(new[] { 1 }, indices.ToArray());
    }

    [TestMethod]
    public void ParameterlessPages_BothNullParameters_Pruned()
    {
        // MainPage / AlbumsPage / etc. take no parameter. Two consecutive
        // instances with both parameters null still match and prune.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(MainPage), null),
            backStack: new[]
            {
                Entry(typeof(AlbumsPage)),
                Entry(typeof(MainPage), null),
            });
        CollectionAssert.AreEqual(new[] { 1 }, indices.ToArray());
    }

    [TestMethod]
    public void NowPlayingFromAlbumA_LeavingToAlbumB_OnlyNowPlayingRemoved()
    {
        // The regression case: user was on AlbumDetail(A), opened NowPlaying,
        // then tapped an album link in NowPlaying's metadata to AlbumDetail(B).
        // Back stack at handler entry: [AlbumDetail(A), NowPlaying]; current
        // AlbumDetail(B). NowPlaying must go; AlbumDetail(A) must stay so Back
        // returns to it.
        var indices = NowPlayingBackStackPolicy.IndicesToRemove(
            current: Entry(typeof(AlbumDetailPage), "album-b"),
            backStack: new[]
            {
                Entry(typeof(AlbumDetailPage), "album-a"),
                Entry(typeof(NowPlayingPage)),
            });
        CollectionAssert.AreEqual(new[] { 1 }, indices.ToArray());
    }
}
