using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class AlbumID3WithSongs : AlbumID3
{
    [JsonPropertyName("song")]
    [XmlElement("song")]
    public Child[]? Song { get; set; }
}
