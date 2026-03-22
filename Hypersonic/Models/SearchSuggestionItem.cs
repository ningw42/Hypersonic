namespace Hypersonic.Models;

public enum SearchResultType
{
    Artist,
    Album,
    Song,
}

public class SearchSuggestionItem
{
    public SearchResultType Type { get; set; }
    public string Id { get; set; } = string.Empty;
    public string PrimaryText { get; set; } = string.Empty;
    public string SecondaryText { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    // Song-only fields for playback
    public string? Artist { get; set; }
    public string? ArtistId { get; set; }
    public string? Album { get; set; }
    public string? AlbumId { get; set; }
    public int Duration { get; set; }
    public Uri? StreamUri { get; set; }
    public Uri? CoverArtUri { get; set; }
}
