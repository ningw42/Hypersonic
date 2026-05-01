namespace Hypersonic.Helpers;

/// <summary>
/// Selector-bar tab on the Now Playing page.
/// </summary>
public enum NowPlayingTab
{
    Lyrics,
    Queue,
}

/// <summary>
/// Pure coordinator for the Queue-tab auto-scroll decision logic. Holds an
/// <c>_isQueueTabActive</c> flag set by <see cref="OnTabSelected"/> and
/// invokes the supplied scroll callback when the queue is refreshed (track
/// change or queue mutation) while the tab is active and the index is in
/// range. The page owns the actual <c>ListView.ScrollIntoView</c> glue.
/// </summary>
public sealed class QueueAutoScrollCoordinator
{
    private readonly Action<int> _scrollIntoView;
    private bool _isQueueTabActive;

    public QueueAutoScrollCoordinator(Action<int> scrollIntoView)
    {
        _scrollIntoView = scrollIntoView;
    }

    public bool IsQueueTabActive => _isQueueTabActive;

    /// <summary>
    /// Called by the page when the SelectorBar selection changes.
    /// </summary>
    public void OnTabSelected(NowPlayingTab tab, int currentEffectiveIndex)
    {
        _isQueueTabActive = tab == NowPlayingTab.Queue;
        if (_isQueueTabActive && currentEffectiveIndex >= 0)
        {
            _scrollIntoView(currentEffectiveIndex);
        }
    }

    /// <summary>
    /// Called by the page on each <c>Player.NowPlayingMetadataChanged</c> AND
    /// each <c>IPlaybackService.QueueChanged</c> while the Queue tab is active.
    /// Both event sources funnel through this method so queue mutations
    /// (shuffle toggle, etc.) also re-scroll the current row into view.
    /// </summary>
    public void OnQueueRefreshed(int effectiveIndex)
    {
        if (_isQueueTabActive && effectiveIndex >= 0)
        {
            _scrollIntoView(effectiveIndex);
        }
    }
}
