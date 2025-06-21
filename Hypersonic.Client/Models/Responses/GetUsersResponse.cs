using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetUsersResponse : SubsonicResponse
{
    [JsonPropertyName("users")]
    [XmlElement("users")]
    public Users Users { get; set; } = null!;
}
