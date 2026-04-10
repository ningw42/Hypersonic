using System.Text.Json.Serialization;
using System.Xml.Serialization;

using Directory = Hypersonic.Client.Models.Entities.Directory;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetMusicDirectoryResponse : SubsonicResponse
{
    [JsonPropertyName("directory")]
    [XmlElement("directory")]
    public Directory Directory { get; set; } = null!;
}
