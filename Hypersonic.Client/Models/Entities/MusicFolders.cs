using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class MusicFolders
{
    [JsonPropertyName("musicFolder")]
    [XmlElement("musicFolder")]
    public MusicFolder[]? MusicFolder { get; set; }
}

