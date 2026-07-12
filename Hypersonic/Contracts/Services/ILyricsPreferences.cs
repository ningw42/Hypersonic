namespace Hypersonic.Contracts.Services;

/// <summary>A provider row as shown in Settings: id, brand name, and whether it is enabled.</summary>
public sealed record LyricsProviderView(string Id, string DisplayName, bool Enabled);

/// <summary>
/// Immutable, point-in-time view of the preferences. A new instance is built and swapped behind a
/// <c>volatile</c> reference on every mutation; a reader captures it once via
/// <see cref="ILyricsPreferences.GetSnapshot"/> and uses it consistently for the duration of one fetch.
/// </summary>
public sealed record LyricsPreferencesSnapshot(
    IReadOnlyList<LyricsProviderView> Providers,
    IReadOnlyList<string> EnabledProviderIds,
    TimeSpan AutoLoadBudget,
    TimeSpan ManualRefreshBudget);

/// <summary>
/// Single source of truth for the user's lyrics provider preferences (enabled set + priority order)
/// and the two fan-out budgets. Consulted live by <c>LyricsService</c> and <c>NowPlayingViewModel</c>;
/// bound by the lyrics settings UI. Persists itself via <c>ILocalSettingsService</c>.
/// </summary>
public interface ILyricsPreferences
{
    /// <summary>Idempotent load + reconcile against the registered providers. Cheap after first call.</summary>
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);

    /// <summary>The current immutable snapshot. Consumers needing several fields consistently (the fan-out
    /// reads both the budget and the enabled set) capture this once rather than reading granular members.</summary>
    LyricsPreferencesSnapshot GetSnapshot();

    /// <summary>Enabled provider ids in priority order — the fan-out set. (Projects from the snapshot.)</summary>
    IReadOnlyList<string> EnabledProviderIds { get; }

    TimeSpan AutoLoadBudget { get; }
    TimeSpan ManualRefreshBudget { get; }

    /// <summary>Registered provider ids in canonical membership order. See
    /// <see cref="DefaultEnabledProviderIds"/> for the providers enabled by reset.</summary>
    IReadOnlyList<string> DefaultProviderOrder { get; }
    /// <summary>The effective provider reset target in priority order: registered preferred defaults when
    /// available, otherwise the first registered provider; empty only when no providers are registered.</summary>
    IReadOnlyList<string> DefaultEnabledProviderIds { get; }
    TimeSpan DefaultAutoLoadBudget { get; }
    TimeSpan DefaultManualRefreshBudget { get; }

    /// <summary>All providers in enabled-first order, with display names, for the settings UI.</summary>
    IReadOnlyList<LyricsProviderView> GetProviders();

    void SetEnabled(string id, bool enabled);
    void Reorder(IReadOnlyList<string> orderedEnabledIds);
    void SetAutoLoadBudget(TimeSpan value);
    void SetManualRefreshBudget(TimeSpan value);
    void ResetProviders();
}
