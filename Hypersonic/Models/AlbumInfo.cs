namespace Hypersonic.Models;

public class AlbumInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? CoverArt { get; set; }
    public int? SongCount { get; set; }
    public TimeSpan? Duration { get; set; }
}
