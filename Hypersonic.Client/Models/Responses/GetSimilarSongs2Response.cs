using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetSimilarSongs2Response : SubsonicResponse
{
    [JsonPropertyName("similarSongs2")]
    [XmlElement("similarSongs2")]
    public SimilarSongs2 SimilarSongs2 { get; set; } = null!;
}

