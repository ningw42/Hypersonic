using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class GetLicenseResponse : SubsonicResponse
{
    [JsonPropertyName("license")]
    [XmlElement("license")]
    public License License
    {
        get; set;
    } = null!;
}
