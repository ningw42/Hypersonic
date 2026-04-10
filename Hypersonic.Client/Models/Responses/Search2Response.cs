using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Hypersonic.Client.Models.Entities;

namespace Hypersonic.Client.Models.Responses;

[XmlRoot("subsonic-response")]
public class Search2Response : SubsonicResponse
{
    [JsonPropertyName("searchResult2")]
    [XmlElement("searchResult2")]
    public SearchResult2 SearchResult2 { get; set; } = null!;
}
