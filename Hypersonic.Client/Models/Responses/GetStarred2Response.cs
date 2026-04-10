using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetStarred2Response : SubsonicResponse
{
    [JsonPropertyName("starred2")]
    [XmlElement("starred2")]
    public Starred2 Starred2 { get; set; } = null!;
}
