using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class AlbumList
{
    [JsonPropertyName("album")]
    [XmlElement("album")]
    public Child[] Album { get; set; }
}
