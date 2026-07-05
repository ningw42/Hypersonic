using Hypersonic.Contracts.Services;

namespace Hypersonic.Tests.Fakes;

/// <summary>Configurable ILyricsPreferences for tests. Reads are settable; mutations record calls.</summary>
public sealed class FakeLyricsPreferences : ILyricsPreferences
{
    public IReadOnlyList<string> EnabledProviderIds { get; set; } = [];
    public TimeSpan AutoLoadBudget { get; set; } = TimeSpan.FromMilliseconds(700);
    public TimeSpan ManualRefreshBudget { get; set; } = TimeSpan.FromMilliseconds(5000);

    public IReadOnlyList<string> DefaultProviderOrder { get; set; } = [];
    public TimeSpan DefaultAutoLoadBudget { get; set; } = TimeSpan.FromMilliseconds(700);
    public TimeSpan DefaultManualRefreshBudget { get; set; } = TimeSpan.FromMilliseconds(5000);

    public List<LyricsProviderView> Providers { get; set; } = [];

    public int EnsureLoadedCount { get; private set; }
    public List<(string Id, bool Enabled)> SetEnabledCalls { get; } = [];
    public List<IReadOnlyList<string>> ReorderCalls { get; } = [];
    public int ResetProvidersCount { get; private set; }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        EnsureLoadedCount++;
        return Task.CompletedTask;
    }

    public IReadOnlyList<LyricsProviderView> GetProviders() => Providers;

    public LyricsPreferencesSnapshot GetSnapshot()
        => new(Providers, EnabledProviderIds, AutoLoadBudget, ManualRefreshBudget);

    public void SetEnabled(string id, bool enabled) => SetEnabledCalls.Add((id, enabled));
    public void Reorder(IReadOnlyList<string> orderedEnabledIds) => ReorderCalls.Add(orderedEnabledIds);
    public void SetAutoLoadBudget(TimeSpan value) => AutoLoadBudget = value;
    public void SetManualRefreshBudget(TimeSpan value) => ManualRefreshBudget = value;

    public void ResetProviders()
    {
        ResetProvidersCount++;
        Providers = DefaultProviderOrder.Select(id => new LyricsProviderView(id, id, true)).ToList();
    }
}
