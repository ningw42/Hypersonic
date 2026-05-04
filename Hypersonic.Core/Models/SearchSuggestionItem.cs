namespace Hypersonic.Core.Models;

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
    public ImageReference? Image { get; set; }

    // Song-only fields for playback
    public string? Artist { get; set; }
    public string? ArtistId { get; set; }
    public string? DisplayArtist { get; set; }
    public List<SongArtistInfo> Artists { get; set; } = [];
    public bool HasStructuredArtists { get; set; }
    public string? Album { get; set; }
    public string? AlbumId { get; set; }
    public int Duration { get; set; }
    public Uri? StreamUri { get; set; }
    public string? Suffix { get; set; }
    public int BitRate { get; set; }
    public int BitDepth { get; set; }
    public int SamplingRate { get; set; }
}
