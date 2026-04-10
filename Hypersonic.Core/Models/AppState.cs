namespace Hypersonic.Core.Models;

public class AppState
{
    public PlaybackState Playback { get; set; } = new();
    public WindowState Window { get; set; } = new();
    public NavigationState Navigation { get; set; } = new();
}

public class PlaybackState
{
    public double Volume { get; set; } = 100;
    public bool IsMuted { get; set; }
    public bool ShuffleEnabled { get; set; }
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
}

public class WindowState
{
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool IsMaximized { get; set; }
}

public class NavigationState
{
    public string? LastPage { get; set; }
}
