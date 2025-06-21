using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetPlaylistsResponse : SubsonicResponse
{
    [JsonPropertyName("playlists")]
    [XmlElement("playlists")]
    public Playlists Playlists { get; set; }
}
