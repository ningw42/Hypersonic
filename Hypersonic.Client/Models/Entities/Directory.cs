using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class Directory
{
    [JsonPropertyName("id")]
    [XmlAttribute("id")]
    public string Id { get; set; }

    [JsonPropertyName("parent")]
    [XmlAttribute("parent")]
    public string Parent { get; set; }

    [JsonPropertyName("name")]
    [XmlAttribute("name")]
    public string Name { get; set; }

    [JsonPropertyName("starred")]
    [XmlAttribute("starred")]
    public string Starred { get; set; }

    [JsonPropertyName("userRating")]
    [XmlAttribute("userRating")]
    public int UserRating { get; set; }

    [JsonPropertyName("averageRating")]
    [XmlAttribute("averageRating")]
    public double AverageRating { get; set; }

    [JsonPropertyName("playCount")]
    [XmlAttribute("playCount")]
    public long PlayCount { get; set; }

    [JsonPropertyName("child")]
    [XmlElement("child")]
    public Child[] Child { get; set; }
}
