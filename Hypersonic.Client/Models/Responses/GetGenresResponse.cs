using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetGenresResponse : SubsonicResponse
{
    [JsonPropertyName("genres")]
    [XmlElement("genres")]
    public Genres Genres { get; set; } = null!;
}
