using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Tests.Fakes;

public sealed class FakeAppStateService : IAppStateService
{
    public AppState State { get; } = new();
    public int SaveSyncCount { get; private set; }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task SaveAsync() => Task.CompletedTask;
    public Task SavePlaybackAsync() => Task.CompletedTask;
    public Task SaveNavigationAsync() => Task.CompletedTask;
    public Task SaveGeneralAsync() => Task.CompletedTask;

    public void SaveSync()
    {
        SaveSyncCount++;
    }
}
