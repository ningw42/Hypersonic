using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class ScanStatus
{
    [JsonPropertyName("scanning")]
    [XmlAttribute("scanning")]
    public bool Scanning { get; set; }

    [JsonPropertyName("count")]
    [XmlAttribute("count")]
    public long? Count { get; set; }
}
