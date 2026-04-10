using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetRandomSongsResponse : SubsonicResponse
{
    [JsonPropertyName("randomSongs")]
    [XmlElement("randomSongs")]
    public RandomSongs RandomSongs { get; set; } = null!;
}
