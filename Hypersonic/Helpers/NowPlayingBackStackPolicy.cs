using Hypersonic.Views;

namespace Hypersonic.Helpers;

/// <summary>
/// Type + parameter projection of a single navigation entry, used by
/// <see cref="NowPlayingBackStackPolicy"/>. Detail pages reuse the same
/// page type for different records (album id, artist id, …) so duplicate
/// detection has to compare the parameter too.
/// </summary>
public readonly record struct NavEntry(Type PageType, object? Parameter);

/// <summary>
/// Pure rule for cleaning the back stack after a navigation completes.
/// Companion to <see cref="NowPlayingTapPolicy"/>: that policy keeps
/// NowPlayingPage off the stack on the way <i>in</i>; this one handles
/// the way <i>out</i> by:
///
/// 1. Stripping every NowPlayingPage entry from the back stack — together
///    with <see cref="NowPlayingTapPolicy"/>, this enforces the invariant
///    that NowPlayingPage is never reached via Back. It's an overlay that
///    lives only at the top of the stack. Type-only comparison: the page
///    has no meaningful parameter.
///
/// 2. Removing CONTIGUOUS top-of-stack duplicates of the current page —
///    fixes the case where the user opens NowPlayingPage from a page and
///    then navigates back to that same page (e.g. Home → NowPlaying → Home),
///    which would otherwise leave the source page as a back-stack entry
///    duplicating the visible page. Pressing Back would then re-navigate
///    to a fresh copy of the same page. Only the topmost contiguous run is
///    removed: a deeper duplicate (e.g. <c>[A, B, A]</c> after navigating
///    to A) is real history and is preserved.
///
///    Type AND parameter must match — AlbumDetail(A) and AlbumDetail(B)
///    are different records and must not collapse. Equality uses
///    <see cref="object.Equals(object?, object?)"/>, which works for the
///    string-id and null parameters this app passes today. If the codebase
///    ever starts passing richer mutable objects, switch to a normalized
///    route key.
/// </summary>
public static class NowPlayingBackStackPolicy
{
    /// <summary>
    /// Returns indices to remove in ascending order. Caller iterates
    /// descending to keep indices valid during removal.
    /// </summary>
    public static IReadOnlyList<int> IndicesToRemove(
        NavEntry? current,
        IReadOnlyList<NavEntry> backStack)
    {
        if (current is { } currentEntry && currentEntry.PageType == typeof(NowPlayingPage))
        {
            return Array.Empty<int>();
        }

        // Phase 1 — collect NowPlayingPage entries (type-only).
        var prune = new HashSet<int>();
        for (var i = 0; i < backStack.Count; i++)
        {
            if (backStack[i].PageType == typeof(NowPlayingPage))
            {
                prune.Add(i);
            }
        }

        // Phase 2 — walk down from the top, skipping anything already pruned,
        // and remove contiguous entries that match current on BOTH type and
        // parameter. Stop at the first non-pruned mismatch, so deeper
        // duplicates remain in history.
        if (current is { } cur)
        {
            for (var i = backStack.Count - 1; i >= 0; i--)
            {
                if (prune.Contains(i))
                {
                    // NowPlayingPage entries are already going away — skip
                    // past them while walking down to the visible portion.
                    continue;
                }
                var entry = backStack[i];
                if (entry.PageType == cur.PageType && Equals(entry.Parameter, cur.Parameter))
                {
                    prune.Add(i);
                }
                else
                {
                    break;
                }
            }
        }

        var sorted = new List<int>(prune);
        sorted.Sort();
        return sorted;
    }
}
