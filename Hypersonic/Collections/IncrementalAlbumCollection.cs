using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;

using Hypersonic.Models;

using Microsoft.UI.Xaml.Data;

using Windows.Foundation;

namespace Hypersonic.Collections;

public class IncrementalAlbumCollection : ObservableCollection<AlbumInfo>, ISupportIncrementalLoading
{
    private readonly Func<uint, Task<IList<AlbumInfo>>> _loadMoreFunc;
    private bool _hasMore = true;

    public bool HasMoreItems => _hasMore;

    public IncrementalAlbumCollection(Func<uint, Task<IList<AlbumInfo>>> loadMoreFunc)
    {
        _loadMoreFunc = loadMoreFunc;
    }

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        return AsyncInfo.Run(async _ =>
        {
            var items = await _loadMoreFunc(count);

            if (items == null || items.Count == 0)
            {
                _hasMore = false;
                return new LoadMoreItemsResult { Count = 0 };
            }

            foreach (var item in items)
            {
                Add(item);
            }

            return new LoadMoreItemsResult { Count = (uint)items.Count };
        });
    }

    public void ResetHasMore()
    {
        _hasMore = true;
    }
}
