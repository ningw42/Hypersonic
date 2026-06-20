using System;

namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// Serialises a Win2D <c>CanvasAnimatedControl</c>'s render-thread draw against UI-thread teardown.
/// The animated control raises <c>Draw</c> on a dedicated game-loop thread, and <c>RemoveFromVisualTree</c>
/// only asks that loop to stop — it does not join the thread — so disposing the draw's device resources
/// (text layouts / format) straight from the <c>Unloaded</c> handler can free them mid-frame, surfacing as
/// an <see cref="ObjectDisposedException"/> out of <c>DrawTextLayout</c>. This gate makes a draw and a
/// teardown mutually exclusive and latches once torn down, so any frame the loop still raises afterwards
/// no-ops instead of touching freed resources.
/// </summary>
/// <remarks>
/// Pure threading logic with no Win2D dependency, so it is unit-tested directly. Both entry points thread a
/// caller-supplied <c>TState</c> to a <c>static</c> delegate so the per-frame draw hop allocates no closure.
/// </remarks>
internal sealed class LyricsRenderGate
{
    private readonly object _lock = new();
    private bool _isTornDown;

    /// <summary>True once <see cref="Teardown{TState}"/> has latched the gate.</summary>
    public bool IsTornDown
    {
        get
        {
            lock (_lock)
            {
                return _isTornDown;
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="draw"/> under the gate unless teardown has latched. Returns <c>true</c> if the
    /// frame ran, <c>false</c> if it was skipped. While it runs, a concurrent <see cref="Teardown{TState}"/>
    /// blocks, so resources the draw reads cannot be disposed mid-frame.
    /// </summary>
    public bool TryDraw<TState>(TState state, Action<TState> draw)
    {
        lock (_lock)
        {
            if (_isTornDown)
            {
                return false;
            }

            draw(state);
            return true;
        }
    }

    /// <summary>
    /// Latches the gate then runs <paramref name="dispose"/> under it. Blocks until any in-flight
    /// <see cref="TryDraw{TState}"/> completes, so disposal never overlaps a draw; afterwards every
    /// <see cref="TryDraw{TState}"/> skips. Latch-once — a second call no-ops and does not re-run dispose.
    /// </summary>
    public void Teardown<TState>(TState state, Action<TState> dispose)
    {
        lock (_lock)
        {
            if (_isTornDown)
            {
                return;
            }

            _isTornDown = true;
            dispose(state);
        }
    }
}
