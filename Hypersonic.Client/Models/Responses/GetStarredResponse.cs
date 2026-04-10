using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetStarredResponse : SubsonicResponse
{
    [JsonPropertyName("starred")]
    [XmlElement("starred")]
    public Starred Starred { get; set; } = null!;
}
