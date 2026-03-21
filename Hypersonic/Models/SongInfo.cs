namespace Hypersonic.Models;

public class SongInfo
{
    // Displayed in track list
    public string Id { get; set; } = string.Empty;
    public int TrackNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Duration { get; set; }
    public int DiscNumber { get; set; }

    // Carried for future use
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public int Year { get; set; }
    public string? CoverArt { get; set; }
    public string? CoverArtUrl { get; set; }
    public int BitRate { get; set; }
    public int BitDepth { get; set; }
    public int SamplingRate { get; set; }
    public string? Suffix { get; set; }
    public long Size { get; set; }
    public string? Path { get; set; }
    public string? AlbumId { get; set; }
    public string? ArtistId { get; set; }
    public string? DisplayArtist { get; set; }
    public Uri? StreamUri { get; set; }
    public Uri? CoverArtUri { get; set; }
}
