using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetNowPlayingResponse : SubsonicResponse
{
    [JsonPropertyName("nowPlaying")]
    [XmlElement("nowPlaying")]
    public NowPlaying NowPlaying { get; set; }
}
