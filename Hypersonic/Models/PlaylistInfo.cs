namespace Hypersonic.Models;

public class PlaylistInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SongCount { get; set; }
    public int Duration { get; set; }
    public string? CoverArt { get; set; }
    public string? CoverArtUrl { get; set; }
    public string SongCountText => SongCount == 1 ? "1 song" : $"{SongCount} songs";
    public string DurationText => TimeSpan.FromSeconds(Duration).ToString(Duration >= 86400 ? @"d\.hh\:mm\:ss" : @"h\:mm\:ss");
}
