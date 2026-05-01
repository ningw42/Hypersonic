using Hypersonic.Helpers;
using Hypersonic.Models;
using Hypersonic.Core.Models;

namespace Hypersonic.Tests.Helpers;

[TestClass]
public class QueueAutoScrollCoordinatorTests
{
    [TestMethod]
    public void OnTabSelected_Queue_WithValidIndex_Scrolls()
    {
        var calls = new List<int>();
        var coord = new QueueAutoScrollCoordinator(calls.Add);

        coord.OnTabSelected(NowPlayingTab.Queue, 5);

        CollectionAssert.AreEqual(new[] { 5 }, calls);
        Assert.IsTrue(coord.IsQueueTabActive);
    }

    [TestMethod]
    public void OnTabSelected_Queue_WithNegativeIndex_DoesNotScroll()
    {
        var calls = new List<int>();
        var coord = new QueueAutoScrollCoordinator(calls.Add);

        coord.OnTabSelected(NowPlayingTab.Queue, -1);

        Assert.AreEqual(0, calls.Count);
        Assert.IsTrue(coord.IsQueueTabActive);
    }

    [TestMethod]
    public void OnTabSelected_Lyrics_DoesNotScroll()
    {
        var calls = new List<int>();
        var coord = new QueueAutoScrollCoordinator(calls.Add);

        coord.OnTabSelected(NowPlayingTab.Lyrics, 3);

        Assert.AreEqual(0, calls.Count);
        Assert.IsFalse(coord.IsQueueTabActive);
    }

    [TestMethod]
    public void OnQueueRefreshed_OnlyScrollsWhenQueueTabActive()
    {
        var calls = new List<int>();
        var coord = new QueueAutoScrollCoordinator(calls.Add);

        // Lyrics active.
        coord.OnQueueRefreshed(7);
        Assert.AreEqual(0, calls.Count);

        coord.OnTabSelected(NowPlayingTab.Queue, 1);
        calls.Clear();

        coord.OnQueueRefreshed(2);
        CollectionAssert.AreEqual(new[] { 2 }, calls);
    }

    [TestMethod]
    public void OnQueueRefreshed_NegativeIndex_NoScroll()
    {
        var calls = new List<int>();
        var coord = new QueueAutoScrollCoordinator(calls.Add);
        coord.OnTabSelected(NowPlayingTab.Queue, 1);
        calls.Clear();

        coord.OnQueueRefreshed(-1);
        Assert.AreEqual(0, calls.Count);
    }

    [TestMethod]
    public void TabToggle_QueueLyricsQueue_ScrollsAgainOnSecondActivation()
    {
        var calls = new List<int>();
        var coord = new QueueAutoScrollCoordinator(calls.Add);

        coord.OnTabSelected(NowPlayingTab.Queue, 1);
        coord.OnTabSelected(NowPlayingTab.Lyrics, -1);
        coord.OnTabSelected(NowPlayingTab.Queue, 4);

        CollectionAssert.AreEqual(new[] { 1, 4 }, calls);
    }

    [TestMethod]
    public void AfterReset_QueueRefreshedDoesNotScroll()
    {
        var calls = new List<int>();
        var coord = new QueueAutoScrollCoordinator(calls.Add);

        coord.OnTabSelected(NowPlayingTab.Queue, 1);
        coord.OnTabSelected(NowPlayingTab.Lyrics, -1);
        calls.Clear();

        coord.OnQueueRefreshed(3);
        Assert.AreEqual(0, calls.Count);
    }
}

[TestClass]
public class QueueScrollResolverTests
{
    private static IReadOnlyList<QueueItem> Make(int n)
    {
        var items = new List<QueueItem>(n);
        for (var i = 0; i < n; i++)
        {
            items.Add(new QueueItem { Track = new PlaybackTrack { Id = $"t{i}" } });
        }
        return items;
    }

    [TestMethod]
    public void ZeroIndex_NonEmpty_ReturnsFirst()
    {
        var items = Make(3);
        var target = QueueScrollResolver.ResolveScrollTarget(items, 0);
        Assert.AreSame(items[0], target);
    }

    [TestMethod]
    public void LastIndex_ReturnsLast()
    {
        var items = Make(3);
        var target = QueueScrollResolver.ResolveScrollTarget(items, 2);
        Assert.AreSame(items[2], target);
    }

    [TestMethod]
    public void NegativeIndex_ReturnsNull()
    {
        var items = Make(3);
        Assert.IsNull(QueueScrollResolver.ResolveScrollTarget(items, -1));
    }

    [TestMethod]
    public void IndexAtCount_ReturnsNull()
    {
        var items = Make(3);
        Assert.IsNull(QueueScrollResolver.ResolveScrollTarget(items, 3));
    }

    [TestMethod]
    public void IndexBeyondCount_ReturnsNull()
    {
        var items = Make(3);
        Assert.IsNull(QueueScrollResolver.ResolveScrollTarget(items, 99));
    }

    [TestMethod]
    public void EmptyList_AnyIndex_ReturnsNull()
    {
        var items = Make(0);
        Assert.IsNull(QueueScrollResolver.ResolveScrollTarget(items, 0));
        Assert.IsNull(QueueScrollResolver.ResolveScrollTarget(items, -1));
        Assert.IsNull(QueueScrollResolver.ResolveScrollTarget(items, 5));
    }
}
