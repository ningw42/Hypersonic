using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class Genre
{
    [JsonPropertyName("value")]
    [XmlAttribute("value")]
    public string Value { get; set; }

    [JsonPropertyName("songCount")]
    [XmlAttribute("songCount")]
    public int SongCount { get; set; }

    [JsonPropertyName("albumCount")]
    [XmlAttribute("albumCount")]
    public int AlbumCount { get; set; }
}
