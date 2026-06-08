using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hypersonic.Core.Models;

/// <summary>
/// A queued/now-playing track. Core stays dependency-free (no CommunityToolkit.Mvvm),
/// so <see cref="INotifyPropertyChanged"/> is hand-rolled and **only
/// <see cref="IsStarred"/></see> notifies — it is the per-track source of truth that
/// queue-row hearts bind to and that <c>FavoritesService</c> broadcasts keep in sync.
/// Every other property is a plain auto-prop set once at materialization.
/// </summary>
public class PlaybackTrack : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? ArtistId { get; set; }
    public string? DisplayArtist { get; set; }
    public string FlatArtistText { get; set; } = string.Empty;
    public List<SongArtistInfo> Artists { get; set; } = [];
    public bool HasStructuredArtists { get; set; }
    public string Album { get; set; } = string.Empty;
    public string? AlbumId { get; set; }
    public int Duration { get; set; }
    public ImageReference? CoverImage { get; set; }
    public Uri? StreamUri { get; set; }
    public int TrackNumber { get; set; }
    public int DiscNumber { get; set; }
    public string? Suffix { get; set; }
    public int BitRate { get; set; }
    public int BitDepth { get; set; }
    public int SamplingRate { get; set; }

    private bool _isStarred;
    public bool IsStarred
    {
        get => _isStarred;
        set
        {
            if (_isStarred == value)
            {
                return;
            }

            _isStarred = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
