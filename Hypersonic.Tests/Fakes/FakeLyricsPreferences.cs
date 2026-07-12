using Hypersonic.Contracts.Services;

namespace Hypersonic.Tests.Fakes;

/// <summary>Configurable ILyricsPreferences for tests. Reads are settable; mutations record calls.</summary>
public sealed class FakeLyricsPreferences : ILyricsPreferences
{
    private IReadOnlyList<string> _defaultEnabledProviderIds = [];

    public IReadOnlyList<string> EnabledProviderIds { get; set; } = [];
    public TimeSpan AutoLoadBudget { get; set; } = TimeSpan.FromMilliseconds(700);
    public TimeSpan ManualRefreshBudget { get; set; } = TimeSpan.FromMilliseconds(5000);

    public IReadOnlyList<string> DefaultProviderOrder { get; set; } = [];
    public IReadOnlyList<string> DefaultEnabledProviderIds
    {
        get => _defaultEnabledProviderIds.Count > 0
            ? _defaultEnabledProviderIds
            : DefaultProviderOrder.Take(1).ToArray();
        set => _defaultEnabledProviderIds = value;
    }
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
        var nameById = Providers.ToDictionary(p => p.Id, p => p.DisplayName, StringComparer.Ordinal);
        string Name(string id) => nameById.TryGetValue(id, out var n) ? n : id;

        var resetTarget = DefaultEnabledProviderIds;
        var enabledSet = new HashSet<string>(resetTarget, StringComparer.Ordinal);
        var enabled = resetTarget.Select(id => new LyricsProviderView(id, Name(id), true));
        var disabled = DefaultProviderOrder.Where(id => !enabledSet.Contains(id))
            .Select(id => new LyricsProviderView(id, Name(id), false));
        Providers = enabled.Concat(disabled).ToList();
    }
}
