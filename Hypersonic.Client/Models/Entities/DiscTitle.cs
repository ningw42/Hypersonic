using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class DiscTitle
{
    [JsonPropertyName("disc")]
    [XmlAttribute("disc")]
    public int Disc { get; set; }

    [JsonPropertyName("title")]
    [XmlAttribute("title")]
    public string? Title { get; set; }
}
