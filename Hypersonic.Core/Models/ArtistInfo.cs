namespace Hypersonic.Core.Models;

public class ArtistInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SortName { get; set; }
    public int AlbumCount { get; set; }
    public int UserRating { get; set; }
    public ImageReference? CoverImage { get; set; }
    public string AlbumCountText => AlbumCount == 1 ? "1 album" : $"{AlbumCount} albums";
}
