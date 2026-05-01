using Hypersonic.Views;

namespace Hypersonic.Helpers;

public sealed record NowPlayingTapDecision(
    bool ShouldNavigate,
    IReadOnlyList<int> BackStackIndicesToRemove);

/// <summary>
/// Pure rules for what happens when the player-bar art / title is tapped.
/// Caller (the <see cref="ShellPage"/> code-behind) handles the WinUI bits:
/// projecting <c>Frame.BackStack</c> into <c>IReadOnlyList&lt;Type&gt;</c>,
/// applying the returned removals (descending), then invoking
/// <c>NavigationService.NavigateTo</c>.
/// </summary>
public static class NowPlayingTapPolicy
{
    public static NowPlayingTapDecision Decide(
        bool hasTrack,
        Type? currentPageType,
        IReadOnlyList<Type> backStackPageTypes)
    {
        if (!hasTrack)
        {
            return new NowPlayingTapDecision(false, Array.Empty<int>());
        }
        if (currentPageType == typeof(NowPlayingPage))
        {
            return new NowPlayingTapDecision(false, Array.Empty<int>());
        }

        var prune = new List<int>();
        for (var i = 0; i < backStackPageTypes.Count; i++)
        {
            if (backStackPageTypes[i] == typeof(NowPlayingPage))
            {
                prune.Add(i);
            }
        }
        return new NowPlayingTapDecision(true, prune);
    }
}
