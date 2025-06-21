using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetPlaylistResponse : SubsonicResponse
{
    [JsonPropertyName("playlist")]
    [XmlElement("playlist")]
    public PlaylistWithSongs Playlist { get; set; }
}
