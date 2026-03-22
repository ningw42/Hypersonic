namespace Hypersonic.Models;

public class PlaybackTrack
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? ArtistId { get; set; }
    public string Album { get; set; } = string.Empty;
    public string? AlbumId { get; set; }
    public int Duration { get; set; }
    public Uri? CoverArtUri { get; set; }
    public Uri? StreamUri { get; set; }
    public int TrackNumber { get; set; }
    public int DiscNumber { get; set; }
}
