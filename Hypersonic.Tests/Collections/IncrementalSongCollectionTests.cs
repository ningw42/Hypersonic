using Hypersonic.Collections;
using Hypersonic.Models;

namespace Hypersonic.Tests.Collections;

// Ideally the LoadBatchCore_* tests would use [UITestMethod] with LoadMoreItemsAsync to
// exercise the full AsyncInfo.Run / IAsyncOperation path, but the WinUI test host crashes
// before [UITestMethod] tests run. The internal LoadBatchCoreAsync method covers the same
// callback → add → HasMoreItems logic without requiring a DispatcherQueue.
//
// To convert back when the test host issue is resolved:
//   1. Change [TestMethod] → [UITestMethod] on the four LoadBatchCore_* tests
//   2. Replace `await collection.LoadBatchCoreAsync()` with `await collection.LoadMoreItemsAsync(2)`
//   3. LoadMoreItemsAsync returns LoadMoreItemsResult (has a .Count property) instead of uint
//   4. The internal LoadBatchCoreAsync method and its tests can then be removed

[TestClass]
public class IncrementalSongCollectionTests
{
    private static SongInfo MakeSong(string id) => new() { Id = id, Title = $"Song {id}" };

    [TestMethod]
    public void Constructor_HasMoreItems_StartsFalse()
    {
        var collection = new IncrementalSongCollection(_ => Task.FromResult<IList<SongInfo>?>([]));
        Assert.IsFalse(collection.HasMoreItems);
    }

    [TestMethod]
    public void Constructor_DefaultPageSize_Is100()
    {
        var collection = new IncrementalSongCollection(_ => Task.FromResult<IList<SongInfo>?>([]));
        Assert.AreEqual(100, collection.PageSize);
    }

    [TestMethod]
    public void Constructor_CustomPageSize_IsStored()
    {
        var collection = new IncrementalSongCollection(_ => Task.FromResult<IList<SongInfo>?>(null), pageSize: 50);
        Assert.AreEqual(50, collection.PageSize);
    }

    [TestMethod]
    public void Reset_ClearsItemsAndSetsHasMoreFalse()
    {
        var collection = new IncrementalSongCollection(_ => Task.FromResult<IList<SongInfo>?>(null));
        collection.AddExternalBatch([MakeSong("1"), MakeSong("2")]);

        collection.Reset();

        Assert.AreEqual(0, collection.Count);
        Assert.IsFalse(collection.HasMoreItems);
    }

    [TestMethod]
    public void AddExternalBatch_FullPage_SetsHasMoreTrue()
    {
        var collection = new IncrementalSongCollection(_ => Task.FromResult<IList<SongInfo>?>(null), pageSize: 2);

        var items = new List<SongInfo> { MakeSong("1"), MakeSong("2") };
        collection.AddExternalBatch(items);

        Assert.AreEqual(2, collection.Count);
        Assert.IsTrue(collection.HasMoreItems);
    }

    [TestMethod]
    public void AddExternalBatch_ShortPage_SetsHasMoreFalse()
    {
        var collection = new IncrementalSongCollection(_ => Task.FromResult<IList<SongInfo>?>(null), pageSize: 3);

        var items = new List<SongInfo> { MakeSong("1"), MakeSong("2") };
        collection.AddExternalBatch(items);

        Assert.AreEqual(2, collection.Count);
        Assert.IsFalse(collection.HasMoreItems);
    }

    [TestMethod]
    public void AddExternalBatch_EmptyList_SetsHasMoreFalse()
    {
        var collection = new IncrementalSongCollection(_ => Task.FromResult<IList<SongInfo>?>(null), pageSize: 2);
        collection.AddExternalBatch([MakeSong("1"), MakeSong("2")]);
        Assert.IsTrue(collection.HasMoreItems);

        collection.AddExternalBatch([]);

        Assert.IsFalse(collection.HasMoreItems);
    }

    [TestMethod]
    public void ReplaceAll_PreservesHasMoreItems()
    {
        var collection = new IncrementalSongCollection(_ => Task.FromResult<IList<SongInfo>?>(null), pageSize: 2);
        collection.AddExternalBatch([MakeSong("1"), MakeSong("2")]);
        Assert.IsTrue(collection.HasMoreItems);

        collection.ReplaceAll([MakeSong("2"), MakeSong("1")]);

        Assert.IsTrue(collection.HasMoreItems);
        Assert.AreEqual(2, collection.Count);
        Assert.AreEqual("2", collection[0].Id);
        Assert.AreEqual("1", collection[1].Id);
    }

    [TestMethod]
    public void ReplaceAll_WhenHasMoreIsFalse_StaysFalse()
    {
        var collection = new IncrementalSongCollection(_ => Task.FromResult<IList<SongInfo>?>(null), pageSize: 3);
        collection.AddExternalBatch([MakeSong("1")]);
        Assert.IsFalse(collection.HasMoreItems);

        collection.ReplaceAll([MakeSong("1")]);

        Assert.IsFalse(collection.HasMoreItems);
    }

    [TestMethod]
    public async Task LoadBatchCore_NullReturn_IsNoOp()
    {
        var callCount = 0;
        var collection = new IncrementalSongCollection(_ =>
        {
            callCount++;
            return Task.FromResult<IList<SongInfo>?>(null);
        }, pageSize: 2);

        collection.AddExternalBatch([MakeSong("1"), MakeSong("2")]);
        var hasMoreBefore = collection.HasMoreItems;

        var loaded = await collection.LoadBatchCoreAsync();

        Assert.AreEqual(0u, loaded);
        Assert.AreEqual(2, collection.Count);
        Assert.AreEqual(hasMoreBefore, collection.HasMoreItems);
        Assert.AreEqual(1, callCount);
    }

    [TestMethod]
    public async Task LoadBatchCore_EmptyList_SetsHasMoreFalse()
    {
        var collection = new IncrementalSongCollection(_ =>
            Task.FromResult<IList<SongInfo>?>(new List<SongInfo>()), pageSize: 2);

        collection.AddExternalBatch([MakeSong("1"), MakeSong("2")]);
        Assert.IsTrue(collection.HasMoreItems);

        await collection.LoadBatchCoreAsync();

        Assert.IsFalse(collection.HasMoreItems);
        Assert.AreEqual(2, collection.Count);
    }

    [TestMethod]
    public async Task LoadBatchCore_FullPage_SetsHasMoreTrue()
    {
        var collection = new IncrementalSongCollection(_ =>
            Task.FromResult<IList<SongInfo>?>(new List<SongInfo> { MakeSong("3"), MakeSong("4") }), pageSize: 2);

        collection.AddExternalBatch([MakeSong("1"), MakeSong("2")]);

        var loaded = await collection.LoadBatchCoreAsync();

        Assert.AreEqual(2u, loaded);
        Assert.AreEqual(4, collection.Count);
        Assert.IsTrue(collection.HasMoreItems);
    }

    [TestMethod]
    public async Task LoadBatchCore_ShortPage_SetsHasMoreFalse()
    {
        var collection = new IncrementalSongCollection(_ =>
            Task.FromResult<IList<SongInfo>?>(new List<SongInfo> { MakeSong("3") }), pageSize: 2);

        collection.AddExternalBatch([MakeSong("1"), MakeSong("2")]);

        var loaded = await collection.LoadBatchCoreAsync();

        Assert.AreEqual(1u, loaded);
        Assert.AreEqual(3, collection.Count);
        Assert.IsFalse(collection.HasMoreItems);
    }
}
