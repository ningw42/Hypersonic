using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetArtistInfo2Response : SubsonicResponse
{
    [JsonPropertyName("artistInfo2")]
    [XmlElement("artistInfo2")]
    public ArtistInfo2 ArtistInfo2 { get; set; } = null!;
}
