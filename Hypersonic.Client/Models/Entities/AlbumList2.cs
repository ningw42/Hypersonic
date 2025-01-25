using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class AlbumList2
{
    [JsonPropertyName("album")]
    [XmlElement("album")]
    public AlbumID3[] Album { get; set; }
}
