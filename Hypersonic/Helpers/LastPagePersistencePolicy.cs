using Hypersonic.ViewModels;
using Hypersonic.Views;

namespace Hypersonic.Helpers;

/// <summary>
/// Pure rules for which pages are persisted to <c>AppState.Navigation.LastPage</c>.
/// Returns the ViewModel key string when the page should be persisted, or
/// <c>null</c> when it should be skipped (NowPlayingPage, null, unknown
/// page types). Callers write the result to AppState only when non-null.
/// </summary>
public static class LastPagePersistencePolicy
{
    private static readonly Dictionary<Type, string> _persistableMap = new()
    {
        [typeof(MainPage)] = typeof(MainViewModel).FullName!,
        [typeof(AlbumsPage)] = typeof(AlbumsViewModel).FullName!,
        [typeof(ArtistsPage)] = typeof(ArtistsViewModel).FullName!,
        [typeof(TracksPage)] = typeof(TracksViewModel).FullName!,
        [typeof(PlaylistsPage)] = typeof(PlaylistsViewModel).FullName!,
        [typeof(SettingsPage)] = typeof(SettingsViewModel).FullName!,
    };

    public static string? GetPersistableViewModelKey(Type? sourcePageType)
    {
        if (sourcePageType == null)
        {
            return null;
        }
        return _persistableMap.TryGetValue(sourcePageType, out var key) ? key : null;
    }
}
