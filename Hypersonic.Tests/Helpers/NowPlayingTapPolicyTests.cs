using Hypersonic.Helpers;
using Hypersonic.Views;

namespace Hypersonic.Tests.Helpers;

[TestClass]
public class NowPlayingTapPolicyTests
{
    [TestMethod]
    public void NoTrack_DoesNotNavigate()
    {
        var d = NowPlayingTapPolicy.Decide(
            hasTrack: false,
            currentPageType: typeof(MainPage),
            backStackPageTypes: Array.Empty<Type>());
        Assert.IsFalse(d.ShouldNavigate);
        Assert.AreEqual(0, d.BackStackIndicesToRemove.Count);
    }

    [TestMethod]
    public void AlreadyOnNowPlaying_NoOp()
    {
        var d = NowPlayingTapPolicy.Decide(
            hasTrack: true,
            currentPageType: typeof(NowPlayingPage),
            backStackPageTypes: new[] { typeof(MainPage) });
        Assert.IsFalse(d.ShouldNavigate);
        Assert.AreEqual(0, d.BackStackIndicesToRemove.Count);
    }

    [TestMethod]
    public void EmptyBackStack_NavigatesNoPrune()
    {
        var d = NowPlayingTapPolicy.Decide(
            hasTrack: true,
            currentPageType: typeof(MainPage),
            backStackPageTypes: Array.Empty<Type>());
        Assert.IsTrue(d.ShouldNavigate);
        Assert.AreEqual(0, d.BackStackIndicesToRemove.Count);
    }

    [TestMethod]
    public void OnePriorNowPlayingInBackStack_PrunedAndNavigates()
    {
        var d = NowPlayingTapPolicy.Decide(
            hasTrack: true,
            currentPageType: typeof(AlbumsPage),
            backStackPageTypes: new[] { typeof(MainPage), typeof(NowPlayingPage) });
        Assert.IsTrue(d.ShouldNavigate);
        CollectionAssert.AreEqual(new[] { 1 }, d.BackStackIndicesToRemove.ToArray());
    }

    [TestMethod]
    public void MultipleNowPlayingEntries_AllPruned()
    {
        var d = NowPlayingTapPolicy.Decide(
            hasTrack: true,
            currentPageType: typeof(AlbumsPage),
            backStackPageTypes: new[]
            {
                typeof(MainPage),
                typeof(NowPlayingPage),
                typeof(AlbumDetailPage),
                typeof(NowPlayingPage),
            });
        Assert.IsTrue(d.ShouldNavigate);
        CollectionAssert.AreEqual(new[] { 1, 3 }, d.BackStackIndicesToRemove.ToArray());
    }

    [TestMethod]
    public void SidebarRoute_PrunesPriorNowPlayingEntry()
    {
        // MainPage -> NowPlayingPage -> AlbumsPage; tap art again from AlbumsPage.
        var d = NowPlayingTapPolicy.Decide(
            hasTrack: true,
            currentPageType: typeof(AlbumsPage),
            backStackPageTypes: new[] { typeof(MainPage), typeof(NowPlayingPage) });
        Assert.IsTrue(d.ShouldNavigate);
        CollectionAssert.AreEqual(new[] { 1 }, d.BackStackIndicesToRemove.ToArray());
    }
}
