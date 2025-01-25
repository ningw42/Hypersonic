using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class SubsonicResponse
{
    [JsonPropertyName("status")]
    [XmlAttribute("status")]
    public string Status { get; set; }

    [JsonPropertyName("version")]
    [XmlAttribute("version")]
    public string Version { get; set; }

    [JsonPropertyName("type")]
    [XmlAttribute("type")]
    public string Type { get; set; }

    [JsonPropertyName("serverVersion")]
    [XmlAttribute("serverVersion")]
    public string ServerVersion { get; set; }

    [JsonPropertyName("openSubsonic")]
    [XmlAttribute("openSubsonic")]
    public bool OpenSubsonic { get; set; }

    [JsonPropertyName("error")]
    [XmlElement("error")]
    public SubsonicError Error { get; set; }
}
