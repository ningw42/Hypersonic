using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class StartScanResponse : SubsonicResponse
{
    [JsonPropertyName("scanStatus")]
    [XmlElement("scanStatus")]
    public ScanStatus ScanStatus { get; set; } = null!;
}
