using Hypersonic.Models;

namespace Hypersonic.Helpers;

/// <summary>
/// Pure resolver: given the current queue items and an effective-queue index,
/// returns the target <see cref="QueueItem"/> or <c>null</c> when the index
/// falls outside <c>items</c>. Mirrors the existing flyout's defensive guard
/// (<c>ShellPage.xaml.cs:272-275</c>) — the index can race a queue rebuild and
/// become stale by the time this runs on the UI thread.
/// </summary>
internal static class QueueScrollResolver
{
    public static QueueItem? ResolveScrollTarget(IReadOnlyList<QueueItem> items, int effectiveIndex)
    {
        if (effectiveIndex < 0 || effectiveIndex >= items.Count)
        {
            return null;
        }
        return items[effectiveIndex];
    }
}
