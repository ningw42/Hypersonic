using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class PlaylistWithSongs : Playlist
{
    [JsonPropertyName("entry")]
    [XmlElement("entry")]
    public Child[]? Entry { get; set; }
}
