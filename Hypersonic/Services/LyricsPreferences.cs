using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services;

/// <summary>
/// Singleton owner of the lyrics provider preferences and the two fan-out budgets. Holds an
/// atomically-swapped immutable snapshot; persists itself under one <see cref="ILocalSettingsService"/>
/// key. See design §Pivotal decisions.
/// </summary>
public sealed class LyricsPreferences : ILyricsPreferences
{
    internal const string SettingsKey = "LyricsPreferences";

    // Canonical default order — pinned in code, not DI registration order (design §Order is enabled-first).
    private static readonly string[] CanonicalOrder =
    [
        Hypersonic.Services.Lyrics.SubsonicLyricsConstants.ProviderId,
        Hypersonic.Core.Services.Lyrics.LrcLibLyricsConstants.ProviderId,
        Hypersonic.Services.Lyrics.AmllLyricsConstants.ProviderId,
        Hypersonic.Core.Services.Lyrics.QQLyricsConstants.ProviderId,
        Hypersonic.Core.Services.Lyrics.NeteaseLyricsConstants.ProviderId,
        Hypersonic.Core.Services.Lyrics.KugouLyricsConstants.ProviderId,
    ];

    private readonly ILocalSettingsService _settings;
    private readonly Dictionary<string, string> _displayNames;
    private readonly string[] _registeredInDefaultOrder;
    private readonly int _defaultAutoMs;
    private readonly int _defaultManualMs;
    private readonly ILogger _log;
    private readonly object _loadLock = new();
    private readonly object _saveLock = new();
    private Task _saveQueue = Task.CompletedTask;

    private volatile LyricsPreferencesSnapshot _snapshot;
    private Task? _loadTask;

    public LyricsPreferences(
        ILocalSettingsService settings,
        IEnumerable<ILyricsProvider> providers,
        IOptions<LyricsOrchestrationOptions> orchestrationOptions,
        ILoggingService logService)
    {
        _settings = settings;
        _displayNames = providers.ToDictionary(p => p.Id, p => p.DisplayName, StringComparer.Ordinal);
        _defaultAutoMs = orchestrationOptions.Value.DefaultFanOutBudgetMs;
        _defaultManualMs = orchestrationOptions.Value.ManualRefreshBudgetMs;
        _log = logService.GetLogger(nameof(LyricsPreferences));

        var canonicalSet = new HashSet<string>(CanonicalOrder, StringComparer.Ordinal);
        _registeredInDefaultOrder = CanonicalOrder.Where(_displayNames.ContainsKey)
            .Concat(_displayNames.Keys.Where(id => !canonicalSet.Contains(id)))
            .ToArray();

        // Defaults snapshot so reads never see uninitialized state before EnsureLoadedAsync.
        _snapshot = BuildSnapshot(LyricsPreferencesReconciler.Reconcile(null, _registeredInDefaultOrder, _defaultAutoMs, _defaultManualMs));
    }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_loadLock)
        {
            return _loadTask ??= LoadCoreAsync();
        }
    }

    private async Task LoadCoreAsync()
    {
        try
        {
            var persisted = await _settings.ReadSettingAsync<LyricsPreferencesState>(SettingsKey).ConfigureAwait(false);
            _snapshot = BuildSnapshot(LyricsPreferencesReconciler.Reconcile(persisted, _registeredInDefaultOrder, _defaultAutoMs, _defaultManualMs));
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to load lyrics preferences; using defaults", exception: ex);
            _snapshot = BuildSnapshot(LyricsPreferencesReconciler.Reconcile(null, _registeredInDefaultOrder, _defaultAutoMs, _defaultManualMs));
        }
    }

    public LyricsPreferencesSnapshot GetSnapshot() => _snapshot;

    public IReadOnlyList<string> EnabledProviderIds => _snapshot.EnabledProviderIds;
    public TimeSpan AutoLoadBudget => _snapshot.AutoLoadBudget;
    public TimeSpan ManualRefreshBudget => _snapshot.ManualRefreshBudget;

    public IReadOnlyList<string> DefaultProviderOrder => _registeredInDefaultOrder;
    public TimeSpan DefaultAutoLoadBudget => TimeSpan.FromMilliseconds(_defaultAutoMs);
    public TimeSpan DefaultManualRefreshBudget => TimeSpan.FromMilliseconds(_defaultManualMs);

    public IReadOnlyList<LyricsProviderView> GetProviders() => _snapshot.Providers;

    /// <summary>Maps a reconciled persisted state to the immutable runtime snapshot — adds display names and
    /// precomputes the enabled-id list. This is the only place the snapshot is constructed.</summary>
    private LyricsPreferencesSnapshot BuildSnapshot(LyricsPreferencesState state)
    {
        var providers = state.Providers
            .Select(p => new LyricsProviderView(p.Id, _displayNames.GetValueOrDefault(p.Id, p.Id), p.Enabled))
            .ToArray();
        var enabledIds = providers.Where(p => p.Enabled).Select(p => p.Id).ToArray();
        return new LyricsPreferencesSnapshot(
            providers,
            enabledIds,
            TimeSpan.FromMilliseconds(state.AutoLoadBudgetMs),
            TimeSpan.FromMilliseconds(state.ManualRefreshBudgetMs));
    }

    // Mutations — implemented in Task 3.
    public void SetEnabled(string id, bool enabled)
    {
        var providers = CurrentRows();
        var index = providers.FindIndex(p => p.Id == id);
        if (index < 0 || providers[index].Enabled == enabled)
        {
            return;
        }

        if (!enabled && providers.Count(p => p.Enabled) <= 1)
        {
            return; // at-least-one-enabled invariant
        }

        var item = providers[index];
        providers.RemoveAt(index);
        item.Enabled = enabled;

        if (enabled)
        {
            providers.Insert(providers.Count(p => p.Enabled), item); // end of enabled run
        }
        else
        {
            providers.Add(item); // sink to the bottom
        }

        Publish(providers);
    }

    public void Reorder(IReadOnlyList<string> orderedEnabledIds)
    {
        var providers = CurrentRows();
        var byId = providers.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var enabled = new List<LyricsProviderPreference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var providerId in orderedEnabledIds)
        {
            if (byId.TryGetValue(providerId, out var p) && p.Enabled && seen.Add(providerId))
            {
                enabled.Add(p);
            }
        }

        // Safety: any enabled provider missing from the requested order keeps its place at the end.
        foreach (var p in providers.Where(p => p.Enabled))
        {
            if (seen.Add(p.Id))
            {
                enabled.Add(p);
            }
        }

        var disabled = providers.Where(p => !p.Enabled);
        Publish(enabled.Concat(disabled).ToList());
    }

    public void SetAutoLoadBudget(TimeSpan value)
    {
        var ms = Math.Clamp((int)value.TotalMilliseconds, LyricsPreferencesReconciler.AutoLoadMinMs, LyricsPreferencesReconciler.AutoLoadMaxMs);
        Publish(CurrentRows(), autoMs: ms);
    }

    public void SetManualRefreshBudget(TimeSpan value)
    {
        var ms = Math.Clamp((int)value.TotalMilliseconds, LyricsPreferencesReconciler.ManualRefreshMinMs, LyricsPreferencesReconciler.ManualRefreshMaxMs);
        Publish(CurrentRows(), manualMs: ms);
    }

    public void ResetProviders()
    {
        var providers = _registeredInDefaultOrder
            .Select(id => new LyricsProviderPreference { Id = id, Enabled = true })
            .ToList();
        Publish(providers);
    }

    /// <summary>Current provider rows as a mutable working copy, derived from the immutable snapshot.</summary>
    private List<LyricsProviderPreference> CurrentRows()
        => _snapshot.Providers.Select(v => new LyricsProviderPreference { Id = v.Id, Enabled = v.Enabled }).ToList();

    /// <summary>Rebuilds the immutable snapshot from the given rows — budgets default to the current
    /// snapshot's unless overridden — and persists the derived state.</summary>
    private void Publish(List<LyricsProviderPreference> providers, int? autoMs = null, int? manualMs = null)
    {
        var state = new LyricsPreferencesState
        {
            Providers = providers,
            AutoLoadBudgetMs = autoMs ?? (int)_snapshot.AutoLoadBudget.TotalMilliseconds,
            ManualRefreshBudgetMs = manualMs ?? (int)_snapshot.ManualRefreshBudget.TotalMilliseconds,
        };

        _snapshot = BuildSnapshot(state);

        lock (_saveLock)
        {
            _saveQueue = SaveAfterPreviousAsync(_saveQueue, state);
        }
    }

    private async Task SaveAfterPreviousAsync(Task previous, LyricsPreferencesState state)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch
        {
            // Keep later writes moving if an earlier one failed.
        }

        try
        {
            await _settings.SaveSettingAsync(SettingsKey, state).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to persist lyrics preferences", exception: ex);
        }
    }

    internal Task WaitForPendingSavesAsync()
    {
        lock (_saveLock)
        {
            return _saveQueue;
        }
    }
}
