using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class License
{
    [JsonPropertyName("valid")]
    [XmlAttribute("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("email")]
    [XmlAttribute("email")]
    public string Email { get; set; }

    [JsonPropertyName("licenseExpires")]
    [XmlAttribute("licenseExpires")]
    public string LicenseExpires { get; set; }

    [JsonPropertyName("trialExpires")]
    [XmlAttribute("trialExpires")]
    public string TrialExpires { get; set; }
}
