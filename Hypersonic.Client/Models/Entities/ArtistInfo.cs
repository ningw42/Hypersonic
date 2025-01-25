using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

/// <seealso cref="https://opensubsonic.netlify.app/docs/responses/artistinfo/"/>
public class ArtistInfo
{
    [JsonPropertyName("biography")]
    [XmlAttribute("biography")]
    public string Biography { get; set; }

    [JsonPropertyName("musicBrainzId")]
    [XmlAttribute("musicBrainzId")]
    public string MusicBrainzId { get; set; }

    [JsonPropertyName("lastFmUrl")]
    [XmlAttribute("lastFmUrl")]
    public string LastFmUrl { get; set; }

    [JsonPropertyName("smallImageUrl")]
    [XmlAttribute("smallImageUrl")]
    public string SmallImageUrl { get; set; }

    [JsonPropertyName("mediumImageUrl")]
    [XmlAttribute("mediumImageUrl")]
    public string MediumImageUrl { get; set; }

    [JsonPropertyName("largeImageUrl")]
    [XmlAttribute("largeImageUrl")]
    public string LargeImageUrl { get; set; }

    [JsonPropertyName("similarArtist")]
    [XmlElement("similarArtist")]
    public Artist[] SimilarArtist { get; set; }
}
