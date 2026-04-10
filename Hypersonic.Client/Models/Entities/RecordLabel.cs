using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class RecordLabel
{
    [JsonPropertyName("name")]
    [XmlAttribute("name")]
    public string Name { get; set; } = null!;
}
