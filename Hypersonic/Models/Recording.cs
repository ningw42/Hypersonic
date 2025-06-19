namespace Hypersonic.Models;

public interface IRecording
{
    string Title { get; set; }
    string Artist { get; }  // Keep as read-only as it's derived from PrimaryArtist
    string Album { get; }   // Keep as read-only as it's derived from AlbumObject
    string StreamUrl { get; set; }
    string Id { get; set; }
    int Duration { get; set; }
    
    // Add new properties for object references
    IArtist PrimaryArtist { get; set; }
    IAlbum AlbumObject { get; set; }
    IList<IArtist> Performers { get; }
}

public class Recording : IRecording
{
    private readonly List<IArtist> _performers = new List<IArtist>();
    
    public string Title { get; set; } = string.Empty;
    public IArtist PrimaryArtist { get; set; } = new Artist();
    public IList<IArtist> Performers => _performers;
    public IAlbum AlbumObject { get; set; } = new Album();
    public string StreamUrl { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public int Duration { get; set; }
    
    // Implement the derived properties
    public string Artist => PrimaryArtist?.Name ?? string.Empty;
    public string Album => AlbumObject?.Title ?? string.Empty;

    public Recording()
    {
    }
    
    public Recording(string id, string title, IArtist primaryArtist, IAlbum album, 
                    string streamUrl = "", int duration = 0)
    {
        Id = id;
        Title = title;
        PrimaryArtist = primaryArtist;
        AlbumObject = album;
        StreamUrl = streamUrl;
        Duration = duration;
        
        // Add the primary artist to performers if not already there
        if (primaryArtist != null && !_performers.Contains(primaryArtist))
        {
            _performers.Add(primaryArtist);
        }
    }
    
    public void AddPerformer(IArtist artist)
    {
        if (artist != null && !_performers.Contains(artist))
        {
            _performers.Add(artist);
        }
    }
    
    public void RemovePerformer(IArtist artist)
    {
        if (artist != null && !artist.Equals(PrimaryArtist)) // Don't remove primary artist
        {
            _performers.Remove(artist);
        }
    }
    
    // Set primary artist and ensure it's in the performers list
    public void SetPrimaryArtist(IArtist artist)
    {
        if (artist == null) return;
        
        PrimaryArtist = artist;
        
        // Add to performers if not already there
        if (!_performers.Contains(artist))
        {
            _performers.Add(artist);
        }
    }
}
