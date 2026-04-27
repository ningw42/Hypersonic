using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;

using Hypersonic.Models;

using Microsoft.UI.Xaml.Data;

using Windows.Foundation;

namespace Hypersonic.Collections;

public class IncrementalSongCollection : ObservableCollection<SongInfo>, ISupportIncrementalLoading
{
    private readonly Func<uint, Task<IList<SongInfo>?>> _loadMoreFunc;
    private bool _hasMore;

    public int PageSize { get; }

    public bool HasMoreItems => _hasMore;

    public Action? OnBatchLoaded { get; set; }

    public IncrementalSongCollection(Func<uint, Task<IList<SongInfo>?>> loadMoreFunc, int pageSize = 100)
    {
        _loadMoreFunc = loadMoreFunc;
        PageSize = pageSize;
    }

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        return AsyncInfo.Run(async _ =>
        {
            var loaded = await LoadBatchCoreAsync();
            return new LoadMoreItemsResult { Count = loaded };
        });
    }

    internal async Task<uint> LoadBatchCoreAsync()
    {
        var items = await _loadMoreFunc((uint)PageSize);

        if (items == null)
        {
            return 0;
        }

        if (items.Count == 0)
        {
            _hasMore = false;
            return 0;
        }

        foreach (var item in items)
        {
            Add(item);
        }

        _hasMore = items.Count >= PageSize;

        OnBatchLoaded?.Invoke();

        return (uint)items.Count;
    }

    public void Reset()
    {
        Clear();
        _hasMore = false;
    }

    public void AddExternalBatch(IList<SongInfo> items)
    {
        foreach (var item in items)
        {
            Add(item);
        }

        _hasMore = items.Count >= PageSize;
    }

    public void ReplaceAll(IList<SongInfo> items)
    {
        Clear();
        foreach (var item in items)
        {
            Add(item);
        }
    }
}
