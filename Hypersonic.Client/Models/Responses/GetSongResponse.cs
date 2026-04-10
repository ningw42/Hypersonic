using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetSongResponse : SubsonicResponse
{
    [JsonPropertyName("song")]
    [XmlElement("song")]
    public Song Song { get; set; } = null!;
}
