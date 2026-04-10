using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class Search3Response : SubsonicResponse
{
    [JsonPropertyName("searchResult3")]
    [XmlElement("searchResult3")]
    public SearchResult3 SearchResult3 { get; set; } = null!;
}
