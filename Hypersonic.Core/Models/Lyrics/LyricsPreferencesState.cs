namespace Hypersonic.Core.Models.Lyrics;

/// <summary>The persisted lyrics-preferences document (one ILocalSettingsService key). Providers are
/// stored in priority order with all enabled entries first, then all disabled.</summary>
public sealed class LyricsPreferencesState
{
    public List<LyricsProviderPreference> Providers { get; set; } = [];
    public int AutoLoadBudgetMs { get; set; }
    public int ManualRefreshBudgetMs { get; set; }
}
