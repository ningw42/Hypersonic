using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class SearchResult3
{
    [JsonPropertyName("artist")]
    [XmlElement("artist")]
    public ArtistID3[]? Artists { get; set; }

    [JsonPropertyName("album")]
    [XmlElement("album")]
    public AlbumID3[]? Album { get; set; }

    [JsonPropertyName("song")]
    [XmlElement("song")]
    public Song[]? Song { get; set; }
}
