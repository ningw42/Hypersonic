using CommunityToolkit.Mvvm.ComponentModel;

using Hypersonic.Core.Models;

namespace Hypersonic.Models;

public partial class SongInfo : ObservableObject
{
    // Displayed in track list
    public string Id { get; set; } = string.Empty;
    public int TrackNumber { get; set; }
    public int PositionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Duration { get; set; }
    public int DiscNumber { get; set; }

    public string Artist { get; set; } = string.Empty;
    public List<SongArtistInfo> Artists { get; set; } = [];
    public bool HasStructuredArtists { get; set; }
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

    // Additional metadata columns
    public long PlayCount { get; set; }
    public int BPM { get; set; }

    // Observable for UI toggle
    [ObservableProperty]
    public partial bool IsStarred { get; set; }

    [ObservableProperty]
    public partial bool IsNowPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    // Display helpers for zero-suppression
    public string BitRateDisplay => BitRate > 0 ? $"{BitRate}" : "";
    public string BPMDisplay => BPM > 0 ? BPM.ToString() : "";
}
