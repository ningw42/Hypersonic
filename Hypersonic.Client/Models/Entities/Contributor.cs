using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class Contributor
{
    [JsonPropertyName("role")]
    [XmlAttribute("role")]
    public string Role { get; set; }

    [JsonPropertyName("subRole")]
    [XmlAttribute("subRole")]
    public string SubRole { get; set; }

    [JsonPropertyName("artist")]
    [XmlElement("artist")]
    public ArtistID3 Artist { get; set; }
}
