using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class NowPlaying
{
    [JsonPropertyName("entry")]
    [XmlElement("entry")]
    public NowPlayingEntry[]? Entry { get; set; }
}
