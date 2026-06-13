namespace Hypersonic.Models;

/// <summary>
/// Fan-out orchestration knobs for <see cref="Hypersonic.Services.LyricsService"/>. Kept separate
/// from <see cref="LyricsCacheOptions"/> because budgets are orchestration, not cache TTL.
/// </summary>
public class LyricsOrchestrationOptions
{
    /// <summary>Wait window for the automatic track-change fetch. Resolves <c>budget == null</c>.</summary>
    public int DefaultFanOutBudgetMs { get; set; } = 700;

    /// <summary>Generous wait window for a user-initiated force-refresh (Phase 3 refresh button).</summary>
    public int ManualRefreshBudgetMs { get; set; } = 5000;
}
