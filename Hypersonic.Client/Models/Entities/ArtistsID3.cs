using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class ArtistsID3
{
    [JsonPropertyName("ignoredArticles")]
    [XmlAttribute("ignoredArticles")]
    public string IgnoredArticles { get; set; }

    [JsonPropertyName("index")]
    [XmlElement("index")]
    public IndexID3[] Index { get; set; }
}
