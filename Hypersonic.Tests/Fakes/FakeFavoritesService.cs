using Hypersonic.Contracts.Services;

namespace Hypersonic.Tests.Fakes;

/// <summary>
/// No-op IFavoritesService for VM tests that don't exercise favorite syncing — they only
/// need the dependency satisfied. Tests that assert star behavior use a real FavoritesService
/// wired to a RecordingSubsonicClient and a fresh WeakReferenceMessenger.
/// </summary>
public sealed class FakeFavoritesService : IFavoritesService
{
    public bool IsStarred(string songId) => false;
    public OperationStamp BeginObservation() => default;
    public Task ToggleAsync(string songId, bool desired) => Task.CompletedTask;
    public Task RefreshAsync() => Task.CompletedTask;
    public void Reconcile(IEnumerable<(string Id, bool Starred)> observed, OperationStamp since) { }
    public void Start() { }
}
