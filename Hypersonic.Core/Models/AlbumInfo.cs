namespace Hypersonic.Core.Models;

public class AlbumInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string DisplayName => string.IsNullOrEmpty(Version) ? Name : $"{Name} ({Version})";
    public string Artist { get; set; } = string.Empty;
    public string? ArtistId { get; set; }
    public int Year { get; set; }
    public string? CoverArt { get; set; }
    public string? CoverArtUrl { get; set; }
    public int? SongCount { get; set; }
    public TimeSpan? Duration { get; set; }

    public string? DisplayArtist { get; set; }
    public List<SongArtistInfo> Artists { get; set; } = [];
    public bool HasStructuredArtists { get; set; }
}
