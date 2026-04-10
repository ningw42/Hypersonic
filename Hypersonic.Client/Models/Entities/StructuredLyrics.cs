using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

[XmlRoot("structuredLyrics")]
public class StructuredLyrics
{
    [JsonPropertyName("displayArtist")]
    [XmlAttribute("displayArtist")]
    public string? DisplayArtist { get; set; }

    [JsonPropertyName("displayTitle")]
    [XmlAttribute("displayTitle")]
    public string? DisplayTitle { get; set; }

    [JsonPropertyName("lang")]
    [XmlAttribute("lang")]
    public string? Lang { get; set; }

    [JsonPropertyName("offset")]
    [XmlAttribute("offset")]
    public long? Offset { get; set; }

    [JsonPropertyName("synced")]
    [XmlAttribute("synced")]
    public bool Synced { get; set; }

    [JsonPropertyName("line")]
    [XmlElement("line")]
    public LyricLine[]? Line { get; set; }
}
