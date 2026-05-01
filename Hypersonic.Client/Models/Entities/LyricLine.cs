using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

[XmlRoot("line")]
public class LyricLine
{
    [JsonPropertyName("start")]
    [XmlIgnore]
    public long? Start { get; set; }

    [JsonPropertyName("value")]
    [XmlText]
    public string? Value { get; set; }
}
