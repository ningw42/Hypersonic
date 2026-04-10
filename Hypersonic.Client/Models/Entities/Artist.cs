using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

/// <summary>
/// This isn't the identical Artist documented in OpenSubsonic, but a merged version with ArtistID3,
/// because the most common server implementation Navidrome doesn't honor the spec,
/// or the documentation doesn't reflect the latest API spec.
/// 
/// Example: getArtist endpoint is supposed to return an Artist which doesn't have 'coverArt' or 'musicBrainzId',
/// but Navidrome's implementation does come with them.
/// </summary>
/// <seealso cref="https://opensubsonic.netlify.app/docs/responses/artist/"/>
[XmlRoot("artist")]
public class Artist
{
    [JsonPropertyName("id")]
    [XmlAttribute("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("name")]
    [XmlAttribute("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("coverArt")]
    [XmlAttribute("coverArt")]
    public string? CoverArt { get; set; }

    [JsonPropertyName("artistImageUrl")]
    [XmlAttribute("artistImageUrl")]
    public string? ArtistImageUrl { get; set; }

    [JsonPropertyName("albumCount")]
    [XmlAttribute("albumCount")]
    public int AlbumCount { get; set; }

    [JsonPropertyName("starred")]
    [XmlAttribute("starred")]
    public string? Starred { get; set; }

    [JsonPropertyName("musicBrainzId")]
    [XmlAttribute("musicBrainzId")]
    public string? MusicBrainzId { get; set; }

    [JsonPropertyName("sortName")]
    [XmlAttribute("sortName")]
    public string? SortName { get; set; }

    [JsonPropertyName("userRating")]
    [XmlAttribute("userRating")]
    public int UserRating { get; set; }

    [JsonPropertyName("averageRating")]
    [XmlAttribute("averageRating")]
    public double AverageRating { get; set; }

    [JsonPropertyName("roles")]
    [XmlElement("roles")]
    public string[]? Roles { get; set; }

    [JsonPropertyName("album")]
    [XmlElement("album")]
    public AlbumID3[]? Album { get; set; }
}
