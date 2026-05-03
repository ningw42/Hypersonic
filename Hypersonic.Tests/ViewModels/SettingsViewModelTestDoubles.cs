using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Tests.ViewModels;

/// <summary>
/// Shared test doubles for SettingsViewModel-focused tests.
/// </summary>
internal sealed class FakeAppStateService : IAppStateService
{
    public AppState State { get; } = new();
    public int GeneralSaveCount { get; private set; }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task SaveAsync() => Task.CompletedTask;
    public Task SavePlaybackAsync() => Task.CompletedTask;
    public Task SaveWindowAsync() => Task.CompletedTask;
    public Task SaveNavigationAsync() => Task.CompletedTask;

    public Task SaveGeneralAsync()
    {
        GeneralSaveCount++;
        return Task.CompletedTask;
    }

    public void SaveSync() { }
}

internal sealed class FakeTrayIconService : ITrayIconService
{
    public int RefreshVisibilityCount { get; private set; }

    public void Initialize() { }
    public void RefreshVisibility() => RefreshVisibilityCount++;
    public void RequestExit() { }
    public void ShowMainWindow() { }
    public void Dispose() { }
}
