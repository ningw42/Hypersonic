using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetLyricsResponse : SubsonicResponse
{
    [JsonPropertyName("lyrics")]
    [XmlElement("lyrics")]
    public Lyrics Lyrics { get; set; }
}
