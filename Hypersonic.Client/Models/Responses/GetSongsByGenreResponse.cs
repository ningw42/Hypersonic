using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetSongsByGenreResponse : SubsonicResponse
{
    [JsonPropertyName("songsByGenre")]
    [XmlElement("songsByGenre")]
    public SongByGenre SongsByGenre { get; set; } = null!;
}
