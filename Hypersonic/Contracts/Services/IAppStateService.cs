using Hypersonic.Models;

namespace Hypersonic.Contracts.Services;

public interface IAppStateService
{
    AppState State { get; }
    Task InitializeAsync();
    Task SaveAsync();
    Task SavePlaybackAsync();
    Task SaveWindowAsync();
    Task SaveNavigationAsync();
    void SaveSync();
}
