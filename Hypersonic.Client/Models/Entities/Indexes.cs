using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class Indexes
{
    [JsonPropertyName("ignoredArticles")]
    [XmlAttribute("ignoredArticles")]
    public string IgnoredArticles { get; set; }

    [JsonPropertyName("lastModified")]
    [XmlAttribute("lastModified")]
    public string LastModified { get; set; }

    [JsonPropertyName("shortcut")]
    [XmlElement("shortcut")]
    public Artist[] Shortcut { get; set; }

    [JsonPropertyName("child")]
    [XmlElement("child")]
    public Child[] Child { get; set; }

    [JsonPropertyName("index")]
    [XmlElement("index")]
    public Index[] Index { get; set; }
}
