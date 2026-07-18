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
