using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class Users
{
    [JsonPropertyName("user")]
    [XmlElement("user")]
    public User[] User { get; set; } = null!;
}