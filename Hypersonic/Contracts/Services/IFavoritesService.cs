namespace Hypersonic.Contracts.Services;

/// <summary>
/// Versions one async operation / store write for the <c>FavoritesService</c> write gate.
/// <see cref="Epoch"/> is the connection lifetime (advanced on every connect/reconnect);
/// <see cref="Seq"/> is a globally-monotonic read/write order.
/// </summary>
public readonly record struct OperationStamp(long Epoch, long Seq);

/// <summary>
/// Single writer of the app's in-memory starred-song set. Owns the API call + status
/// check + optimistic revert in exactly one place and broadcasts every change as a batched
/// <c>FavoritesChangedMessage</c> delta. Songs only; album/artist stars are id-based and
/// additive later. All members run on the UI thread.
/// </summary>
public interface IFavoritesService
{
    /// <summary>Store lookup — used to hydrate newly-materialized instances and overlay loaded lists.</summary>
    bool IsStarred(string songId);

    /// <summary>Capture (epoch + seq) before a page fetch; pass the result to <see cref="Reconcile"/>.</summary>
    OperationStamp BeginObservation();

    /// <summary>Coalesced per-id toggle: optimistic store-first broadcast, then API call, then revert on failure.</summary>
    Task ToggleAsync(string songId, bool desired);

    /// <summary><c>getStarred2</c> → gated diff against the store → store-first → up to two batched broadcasts.</summary>
    Task RefreshAsync();

    /// <summary>Adopt a page's fresh per-song observations through the write gate, broadcasting the survivors.</summary>
    void Reconcile(IEnumerable<(string Id, bool Starred)> observed, OperationStamp since);

    /// <summary>Advance the epoch, reset connection-scoped state, capture the dispatcher + poll timer, and seed.</summary>
    void Start();
}
