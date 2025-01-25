using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetTopSongsResponse : SubsonicResponse
{
    [JsonPropertyName("topSongs")]
    [XmlElement("topSongs")]
    public TopSongs TopSongs { get; set; }
}


