namespace Hypersonic.Models;

public interface IArtist
{
    /// <summary>
    /// Identifier
    /// </summary>
    string Id { get; set; }

    /// <summary>
    /// Presentation name of the artist.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Sorting name of the artist, used for sorting purposes.
    /// </summary>
    string SortName { get; set; }

    /// <summary>
    /// Artist image URI, typically a URL to the artist's image.
    /// </summary>
    string ImageUri { get; set; }
}

public class Artist : IArtist
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SortName { get; set; } = string.Empty;
    public string ImageUri { get; set; } = string.Empty;
    
    public Artist()
    {
    }

    public Artist(string id, string name, string sortName = "", string imageUri = "")
    {
        Id = id;
        Name = name;
        SortName = string.IsNullOrEmpty(sortName) ? name : sortName;
        ImageUri = imageUri;
    }
}
