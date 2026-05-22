using Hypersonic.Core.Models;

namespace Hypersonic.Contracts.Services;

/// <summary>
/// Holds the home page's "Random Picks" album list for the lifetime of the app run.
/// First read triggers a fetch; subsequent reads return the cached list. A single in-flight
/// fetch is enforced (single-flight) so concurrent callers cannot trigger overlapping API calls.
/// </summary>
public interface IRandomPicksCacheService
{
    /// <summary>
    /// Snapshot of the currently cached picks, or an empty list if none are loaded yet.
    /// Always reflects the most recently successful fetch.
    /// </summary>
    IReadOnlyList<AlbumInfo> CurrentPicks { get; }

    /// <summary>
    /// True while a fetch is in flight (either the initial load or a refresh).
    /// </summary>
    bool IsLoading { get; }

    /// <summary>
    /// True once at least one fetch has completed <em>successfully</em>. A failed first attempt
    /// leaves this <c>false</c> so the next read / navigation retries — the cache is only
    /// considered "loaded" when it actually has picks (or has confirmed empty picks from a
    /// successful response).
    /// </summary>
    bool HasLoaded { get; }

    /// <summary>
    /// Raised whenever <see cref="CurrentPicks"/> changes (after a successful fetch).
    /// Not raised on failure or on cache hits. Marshalling onto the UI thread is the
    /// subscriber's responsibility.
    /// </summary>
    event EventHandler? PicksChanged;

    /// <summary>
    /// Raised whenever <see cref="IsLoading"/> changes. Allows UI to enable/disable affordances.
    /// </summary>
    event EventHandler? LoadingChanged;

    /// <summary>
    /// Returns the cached picks. If no fetch has completed yet, kicks off (or joins) the
    /// in-flight fetch and returns its result. On fetch failure, returns the previous list
    /// (or an empty list if there is none) and logs the error.
    /// </summary>
    Task<IReadOnlyList<AlbumInfo>> GetOrFetchAsync();

    /// <summary>
    /// Forces a fresh fetch. If a fetch is already in flight (either an initial load or a
    /// prior refresh), this is a no-op: the in-flight call wins, and <see cref="PicksChanged"/>
    /// fires once when it completes. On failure, retains the previous list and logs the error.
    /// </summary>
    Task RefreshAsync();
}
