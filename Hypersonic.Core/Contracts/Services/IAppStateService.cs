using Hypersonic.Core.Models;

namespace Hypersonic.Core.Contracts.Services;

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
