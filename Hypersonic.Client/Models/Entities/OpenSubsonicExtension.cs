using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class OpenSubsonicExtension
{
    [JsonPropertyName("name")]
    [XmlAttribute("name")]
    public string Name { get; set; }

    [JsonPropertyName("versions")]
    [XmlElement("versions")]
    public int[] Versions { get; set; }
}
