using Hypersonic.Views;

namespace Hypersonic.Helpers;

public enum NowPlayingTapAction
{
    None,
    Navigate,
    GoBack,
}

public sealed record NowPlayingTapDecision(
    NowPlayingTapAction Action,
    IReadOnlyList<int> BackStackIndicesToRemove);

/// <summary>
/// Pure rules for what happens when the player-bar art / title is tapped.
/// Caller (the <see cref="ShellPage"/> code-behind) handles the WinUI bits:
/// projecting <c>Frame.BackStack</c> into <c>IReadOnlyList&lt;Type&gt;</c>,
/// applying the returned removals (descending) for <see cref="NowPlayingTapAction.Navigate"/>,
/// then invoking <c>NavigationService.NavigateTo</c> or
/// <c>NavigationService.GoBack</c> as the action dictates.
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
            return new NowPlayingTapDecision(NowPlayingTapAction.None, Array.Empty<int>());
        }
        if (currentPageType == typeof(NowPlayingPage))
        {
            return new NowPlayingTapDecision(NowPlayingTapAction.GoBack, Array.Empty<int>());
        }

        var prune = new List<int>();
        for (var i = 0; i < backStackPageTypes.Count; i++)
        {
            if (backStackPageTypes[i] == typeof(NowPlayingPage))
            {
                prune.Add(i);
            }
        }
        return new NowPlayingTapDecision(NowPlayingTapAction.Navigate, prune);
    }
}
