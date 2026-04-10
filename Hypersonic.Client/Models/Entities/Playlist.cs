using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class Playlist
{
    [JsonPropertyName("id")]
    [XmlAttribute("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("name")]
    [XmlAttribute("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("comment")]
    [XmlAttribute("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("owner")]
    [XmlAttribute("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("public")]
    [XmlAttribute("public")]
    public bool Public { get; set; }

    [JsonPropertyName("songCount")]
    [XmlAttribute("songCount")]
    public int SongCount { get; set; }

    [JsonPropertyName("duration")]
    [XmlAttribute("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("created")]
    [XmlAttribute("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("changed")]
    [XmlAttribute("changed")]
    public DateTime Changed { get; set; }

    [JsonPropertyName("coverArt")]
    [XmlAttribute("coverArt")]
    public string? CoverArt { get; set; }

    [JsonPropertyName("allowedUser")]
    [XmlElement("allowedUser")]
    public string[]? AllowedUser { get; set; }
}
