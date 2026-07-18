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

internal sealed class FakeImageCacheService : IImageService
{
    public ImageCacheStats Stats { get; set; } = new(0, 0);
    public int ClearCallCount { get; private set; }

    public Task<Uri?> GetImageAsync(
        ImageReference? image,
        ImageRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Uri?>(null);
    }

    public Task<ImageCacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Stats);
    }

    public Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCallCount++;
        Stats = new ImageCacheStats(0, 0);
        return Task.CompletedTask;
    }
}
