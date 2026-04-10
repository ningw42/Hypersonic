using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetIndexesResponse : SubsonicResponse
{
    [JsonPropertyName("indexes")]
    [XmlElement("indexes")]
    public Indexes Indexes { get; set; } = null!;
}
