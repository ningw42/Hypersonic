using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetArtistInfoResponse : SubsonicResponse
{
    [JsonPropertyName("artistInfo")]
    [XmlElement("artistInfo")]
    public ArtistInfo ArtistInfo { get; set; }
}
