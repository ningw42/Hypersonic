using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class AlbumID3
{
    [JsonPropertyName("id")]
    [XmlAttribute("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    [XmlAttribute("name")]
    public string Name { get; set; }

    [JsonPropertyName("artist")]
    [XmlAttribute("artist")]
    public string Artist { get; set; }

    [JsonPropertyName("artistId")]
    [XmlAttribute("artistId")]
    public string ArtistId { get; set; }

    [JsonPropertyName("coverArt")]
    [XmlAttribute("coverArt")]
    public string CoverArt { get; set; }

    [JsonPropertyName("songCount")]
    [XmlAttribute("songCount")]
    public int SongCount { get; set; }

    [JsonPropertyName("duration")]
    [XmlAttribute("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("playCount")]
    [XmlAttribute("playCount")]
    public long PlayCount { get; set; }

    [JsonPropertyName("created")]
    [XmlAttribute("created")]
    public string Created { get; set; }

    [JsonPropertyName("starred")]
    [XmlAttribute("starred")]
    public string Starred { get; set; }

    [JsonPropertyName("year")]
    [XmlAttribute("year")]
    public int Year { get; set; }

    [JsonPropertyName("genre")]
    [XmlAttribute("genre")]
    public string Genre { get; set; }

    [JsonPropertyName("played")]
    [XmlAttribute("played")]
    public string Played { get; set; }

    [JsonPropertyName("userRating")]
    [XmlAttribute("userRating")]
    public int UserRating { get; set; }

    [JsonPropertyName("recordLabels")]
    [XmlElement("recordLabels")]
    public RecordLabel[] RecordLabels { get; set; }

    [JsonPropertyName("musicBrainzId")]
    [XmlAttribute("musicBrainzId")]
    public string MusicBrainzId { get; set; }

    [JsonPropertyName("genres")]
    [XmlElement("genres")]
    public ItemGenre[] Genres { get; set; }

    [JsonPropertyName("artists")]
    [XmlElement("artists")]
    public ArtistID3[] Artists { get; set; }

    [JsonPropertyName("displayArtist")]
    [XmlAttribute("displayArtist")]
    public string DisplayArtist { get; set; }

    [JsonPropertyName("releaseTypes")]
    [XmlElement("releaseTypes")]
    public string[] ReleaseTypes { get; set; }

    [JsonPropertyName("moods")]
    [XmlElement("moods")]
    public string[] Moods { get; set; }

    [JsonPropertyName("sortName")]
    [XmlAttribute("sortName")]
    public string SortName { get; set; }

    [JsonPropertyName("originalReleaseDate")]
    [XmlElement("originalReleaseDate")]
    public ItemDate OriginalReleaseDate { get; set; }

    [JsonPropertyName("releaseDate")]
    [XmlElement("releaseDate")]
    public ItemDate ReleaseDate { get; set; }

    public bool IsCompilation { get; set; }

    [JsonPropertyName("discTitles")]
    [XmlElement("discTitles")]
    public DiscTitle[] DiscTitles { get; set; }
}
