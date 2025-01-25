using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class Index
{
    [JsonPropertyName("name")]
    [XmlAttribute("name")]
    public string Name { get; set; }

    [JsonPropertyName("artist")]
    [XmlElement("artist")]
    public Artist[] Artist { get; set; }
}
