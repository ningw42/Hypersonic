using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetAlbumResponse : SubsonicResponse
{
    [JsonPropertyName("album")]
    [XmlElement("album")]
    public AlbumID3WithSongs Album { get; set; } = null!;
}
