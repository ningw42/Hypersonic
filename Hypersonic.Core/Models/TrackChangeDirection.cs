namespace Hypersonic.Core.Models;

/// <summary>
/// Direction of a track change. Carried by <see cref="TrackChangedEventArgs"/>
/// so the playback layer (the only layer that authoritatively knows the cause)
/// can tell consumers — including external entry points like SMTC and the
/// taskbar — which direction triggered the change.
/// </summary>
public enum TrackChangeDirection
{
    /// <summary>
    /// No-op / sentinel. Should not appear in real <c>TrackChanged</c> events;
    /// reserved for default-initialized state in consumers that need a falsy
    /// value before the first event.
    /// </summary>
    None,

    /// <summary>User-initiated forward navigation, or auto-advance at end of track.</summary>
    Next,

    /// <summary>User-initiated backward navigation.</summary>
    Previous,

    /// <summary>Direct jump (queue popup click, JumpToTrack, or fresh queue start).</summary>
    Jump,
}
