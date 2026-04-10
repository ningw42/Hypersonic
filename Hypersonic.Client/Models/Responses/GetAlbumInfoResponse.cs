using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetAlbumInfoResponse : SubsonicResponse
{
    [JsonPropertyName("albumInfo")]
    [XmlElement("albumInfo")]
    public AlbumInfo AlbumInfo { get; set; } = null!;
}
