using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class NowPlayingEntry : Child
{
    [JsonPropertyName("username")]
    [XmlAttribute("username")]
    public string? UserName { get; set; }

    [JsonPropertyName("minutesAgo")]
    [XmlAttribute("minutesAgo")]
    public string? MinutesAgo { get; set; }

    [JsonPropertyName("playerId")]
    [XmlAttribute("playerId")]
    public bool PlayerId { get; set; }

    [JsonPropertyName("playerName")]
    [XmlAttribute("playerName")]
    public string? PlayerName { get; set; }
}
