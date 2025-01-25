using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class ReplayGain
{
    [JsonPropertyName("trackGain")]
    [XmlAttribute("trackGain")]
    public double TrackGain { get; set; }

    [JsonPropertyName("albumGain")]
    [XmlAttribute("albumGain")]
    public double AlbumGain { get; set; }

    [JsonPropertyName("trackPeak")]
    [XmlAttribute("trackPeak")]
    public double TrackPeak { get; set; }

    [JsonPropertyName("albumPeak")]
    [XmlAttribute("albumPeak")]
    public double AlbumPeak { get; set; }

    [JsonPropertyName("baseGain")]
    [XmlAttribute("baseGain")]
    public double BaseGain { get; set; }

    [JsonPropertyName("fallbackGain")]
    [XmlAttribute("fallbackGain")]
    public double FallbackGain { get; set; }
}
