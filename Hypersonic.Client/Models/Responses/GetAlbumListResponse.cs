using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetAlbumListResponse : SubsonicResponse
{
    [JsonPropertyName("albumList")]
    [XmlElement("albumList")]
    public AlbumList AlbumList { get; set; } = null!;
}
