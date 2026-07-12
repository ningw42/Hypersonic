using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Pure normalization of a persisted <see cref="LyricsPreferencesState"/> against the currently
/// registered providers. No WinUI/IO. See design §Reconcile.
/// </summary>
public static class LyricsPreferencesReconciler
{
    public const int AutoLoadMinMs = 300;
    public const int AutoLoadMaxMs = 3000;
    public const int ManualRefreshMinMs = 1000;
    public const int ManualRefreshMaxMs = 15000;

    /// <param name="persisted">Loaded state, or null on first run.</param>
    /// <param name="registeredIdsInDefaultOrder">Registered provider ids in canonical membership order
    /// (known-canonical first, then extras). Doubles as the membership set for placing disabled providers.</param>
    /// <param name="defaultEnabledInPriorityOrder">Ids enabled by default, in priority order — applied only
    /// when <paramref name="persisted"/> is null. Missing ids append disabled when persisted state exists.</param>
    public static LyricsPreferencesState Reconcile(
        LyricsPreferencesState? persisted,
        IReadOnlyList<string> registeredIdsInDefaultOrder,
        IReadOnlyList<string> defaultEnabledInPriorityOrder,
        int defaultAutoLoadMs,
        int defaultManualRefreshMs)
    {
        var registered = new HashSet<string>(registeredIdsInDefaultOrder, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<LyricsProviderPreference>();

        // 1. Keep persisted rows still registered, in saved order; drop unknown/duplicate.
        if (persisted?.Providers is { } persistedRows)
        {
            foreach (var row in persistedRows)
            {
                if (row is null || string.IsNullOrEmpty(row.Id) || !registered.Contains(row.Id) || !seen.Add(row.Id))
                {
                    continue;
                }

                rows.Add(new LyricsProviderPreference { Id = row.Id, Enabled = row.Enabled });
            }
        }

        // 2a. On first run, append default-enabled ids in priority order, enabled.
        if (persisted is null)
        {
            foreach (var id in defaultEnabledInPriorityOrder)
            {
                if (registered.Contains(id) && seen.Add(id))
                {
                    rows.Add(new LyricsProviderPreference { Id = id, Enabled = true });
                }
            }
        }

        // 2b. Append any remaining registered ids, in canonical order, disabled by default
        //     (a new/opinionated-off provider appears but stays out of the fan-out until the user enables it).
        foreach (var id in registeredIdsInDefaultOrder)
        {
            if (seen.Add(id))
            {
                rows.Add(new LyricsProviderPreference { Id = id, Enabled = false });
            }
        }

        // 3. At-least-one-enabled: enable the first entry if reconciliation left none.
        if (rows.Count > 0 && !rows.Any(p => p.Enabled))
        {
            rows[0].Enabled = true;
        }

        // 4. Enabled-first stable ordering: enabled entries keep their order, then disabled keep theirs.
        var ordered = rows.Where(p => p.Enabled).Concat(rows.Where(p => !p.Enabled)).ToList();

        return new LyricsPreferencesState
        {
            Providers = ordered,
            AutoLoadBudgetMs = ClampOrDefault(persisted?.AutoLoadBudgetMs, defaultAutoLoadMs, AutoLoadMinMs, AutoLoadMaxMs),
            ManualRefreshBudgetMs = ClampOrDefault(persisted?.ManualRefreshBudgetMs, defaultManualRefreshMs, ManualRefreshMinMs, ManualRefreshMaxMs),
        };
    }

    private static int ClampOrDefault(int? value, int fallback, int min, int max)
    {
        // Missing (first run) or a blank/zero record → default; otherwise clamp into range.
        if (value is null or 0)
        {
            return fallback;
        }

        return Math.Clamp(value.Value, min, max);
    }
}
