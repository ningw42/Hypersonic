using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetAlbumList2Response : SubsonicResponse
{
    [JsonPropertyName("albumList2")]
    [XmlElement("albumList2")]
    public AlbumList2 AlbumList2 { get; set; } = null!;
}
