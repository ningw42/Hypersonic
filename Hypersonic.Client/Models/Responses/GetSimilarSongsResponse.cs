using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetSimilarSongsResponse : SubsonicResponse
{
    [JsonPropertyName("similarSongs")]
    [XmlElement("similarSongs")]
    public SimilarSongs SimilarSongs { get; set; } = null!;
}
