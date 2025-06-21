using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetUserResponse : SubsonicResponse
{
    [JsonPropertyName("user")]
    [XmlElement("user")]
    public User User { get; set; } = null!;
}
