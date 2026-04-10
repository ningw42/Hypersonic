using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetArtistResponse : SubsonicResponse
{
    [JsonPropertyName("artist")]
    [XmlElement("artist")]
    public Artist Artist { get; set; } = null!;
}
