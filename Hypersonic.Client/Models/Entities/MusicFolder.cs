using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.JsonConverters;

namespace Hypersonic.Client.Models.Entities;

public class MusicFolder
{
    /// <remarks>
    /// Navidrome doesn't honor the OpenSubsonic/Subsonic JSON contract,
    /// returning JSON number token instead of JSON string token.
    /// </remarks>
    [JsonPropertyName("id")]
    [XmlAttribute("id")]
    [JsonConverter(typeof(ForgivingStringConverter))]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    [XmlAttribute("name")]
    public string Name { get; set; }
}
