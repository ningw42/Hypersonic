using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class SearchResult2
{
    [JsonPropertyName("artist")]
    [XmlElement("artist")]
    public Artist[] Artists { get; set; }

    [JsonPropertyName("album")]
    [XmlElement("album")]
    public Child[] Album { get; set; }

    [JsonPropertyName("song")]
    [XmlElement("song")]
    public Child[] Song { get; set; }
}
