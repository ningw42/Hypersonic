using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetOpenSubsonicExtensionsResponse : SubsonicResponse
{
    [JsonPropertyName("openSubsonicExtensions")]
    [XmlElement("openSubsonicExtensions")]
    public OpenSubsonicExtension[] OpenSubsonicExtensions { get; set; } = null!;
}
