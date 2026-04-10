using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetArtistsResponse : SubsonicResponse
{
    [JsonPropertyName("artists")]
    [XmlElement("artists")]
    public ArtistsID3 Artists { get; set; } = null!;
}
