using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class ItemDate
{
    [JsonPropertyName("year")]
    [XmlAttribute("year")]
    public int Year { get; set; }

    [JsonPropertyName("month")]
    [XmlAttribute("month")]
    public int Month { get; set; }

    [JsonPropertyName("day")]
    [XmlAttribute("day")]
    public int Day { get; set; }
}
