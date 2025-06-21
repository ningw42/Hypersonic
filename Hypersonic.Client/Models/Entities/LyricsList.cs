using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

[XmlRoot("lyricsList")]
public class LyricsList
{
    [JsonPropertyName("structuredLyrics")]
    [XmlElement("structuredLyrics")]
    public StructuredLyrics[] StructuredLyrics { get; set; }
}
