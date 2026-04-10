using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

/// <see cref="https://opensubsonic.netlify.app/docs/responses/error/"/>
public class SubsonicError
{
    [JsonPropertyName("code")]
    [XmlAttribute("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    [XmlAttribute("message")]
    public string Message { get; set; } = null!;

    [JsonPropertyName("helpUrl")]
    [XmlAttribute("helpUrl")]
    public string? HelpUrl { get; set; }
}
