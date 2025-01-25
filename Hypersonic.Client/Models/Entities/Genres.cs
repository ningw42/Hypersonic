using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class Genres
{
    [JsonPropertyName("genres")]
    [XmlElement("genres")]
    public Genre[] Genre { get; set; }
}

