using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

[XmlRoot("lyrics")]
public class Lyrics
{
    [JsonPropertyName("artist")]
    [XmlAttribute("artist")]
    public string? Artist { get; set; }

    [JsonPropertyName("title")]
    [XmlAttribute("title")]
    public string? Title { get; set; }

    [JsonPropertyName("value")]
    [XmlText]
    public string? Value { get; set; }
}
