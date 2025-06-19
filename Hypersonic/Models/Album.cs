namespace Hypersonic.Models;

public interface IAlbum
{
    string Id { get; set; }
    string Title { get; set; }
    IArtist AlbumArtist { get; set; }
    string CoverArtUri { get; set; }
    int Runtime { get; set; }
    string[] Genre { get; set; }
    IList<IDisc> Discs { get; set; }
}

public class Album : IAlbum
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public IArtist AlbumArtist { get; set; } = new Artist();
    public string CoverArtUri { get; set; } = string.Empty;
    public int Runtime { get; set; }
    public string[] Genre { get; set; } = Array.Empty<string>();
    public IList<IDisc> Discs { get; set; } = new List<IDisc>();

    public Album()
    {
    }

    public Album(string id, string title, IArtist albumArtist, string coverArtUri = "", 
                int runtime = 0, string[]? genre = null, IList<IDisc>? discs = null)
    {
        Id = id;
        Title = title;
        AlbumArtist = albumArtist;
        CoverArtUri = coverArtUri;
        Runtime = runtime;
        Genre = genre ?? Array.Empty<string>();
        Discs = discs ?? new List<IDisc>();
    }
    
    public void AddDisc(IDisc disc)
    {
        if (disc != null)
        {
            Discs.Add(disc);
            // Recalculate runtime when adding a disc
            RecalculateRuntime();
        }
    }
    
    public void RecalculateRuntime()
    {
        int totalDuration = 0;
        foreach (var disc in Discs)
        {
            foreach (var recording in disc.Recordings)
            {
                totalDuration += recording.Duration;
            }
        }
        Runtime = totalDuration;
    }
}
