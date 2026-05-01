namespace Hypersonic.Helpers;

/// <summary>
/// Owns the single <c>_subscribed</c> flag that gates the two Queue-tab
/// subscriptions (<c>Player.NowPlayingMetadataChanged</c> and
/// <c>IPlaybackService.QueueChanged</c>) as one atomic unit. Constructed once
/// with <see cref="_attachAll"/> / <see cref="_detachAll"/> callbacks that
/// wrap both <c>+=</c> calls / both <c>-=</c> calls together. The page
/// constructs one instance in its constructor and forwards from
/// <c>SelectionChanged</c> / <c>Unloaded</c>.
/// </summary>
public sealed class QueueTabSubscriptionGate
{
    private readonly Action _attachAll;
    private readonly Action _detachAll;
    private bool _subscribed;

    public QueueTabSubscriptionGate(Action attachAll, Action detachAll)
    {
        _attachAll = attachAll;
        _detachAll = detachAll;
    }

    /// <summary>
    /// Exposed so tests can assert directly; production callers only need
    /// behavioral checks.
    /// </summary>
    internal bool IsSubscribed => _subscribed;

    public void Attach()
    {
        if (_subscribed)
        {
            return;
        }
        _subscribed = true;
        _attachAll();
    }

    public void Detach()
    {
        if (!_subscribed)
        {
            return;
        }
        _subscribed = false;
        _detachAll();
    }
}
