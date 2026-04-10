using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class Playlists
{
    [JsonPropertyName("playlist")]
    [XmlElement("playlist")]
    public Playlist[]? Playlist { get; set; }
}
