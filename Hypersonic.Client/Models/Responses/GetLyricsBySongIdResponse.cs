using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetLyricsBySongIdResponse : SubsonicResponse
{
    [JsonPropertyName("lyricsList")]
    [XmlElement("lyricsList")]
    public LyricsList LyricsList { get; set; } = null!;
}
