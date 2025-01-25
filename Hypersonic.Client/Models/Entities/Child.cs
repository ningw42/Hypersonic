using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class Child
{
    [JsonPropertyName("id")]
    [XmlAttribute("id")]
    public string Id { get; set; }

    [JsonPropertyName("parent")]
    [XmlAttribute("parent")]
    public string Parent { get; set; }

    [JsonPropertyName("isDir")]
    [XmlAttribute("isDir")]
    public bool IsDir { get; set; }

    [JsonPropertyName("title")]
    [XmlAttribute("title")]
    public string Title { get; set; }

    [JsonPropertyName("album")]
    [XmlAttribute("album")]
    public string Album { get; set; }

    [JsonPropertyName("artist")]
    [XmlAttribute("artist")]
    public string Artist { get; set; }

    [JsonPropertyName("track")]
    [XmlAttribute("track")]
    public int Track { get; set; }
    
    [JsonPropertyName("year")]
    [XmlAttribute("year")]
    public int Year { get; set; }

    [JsonPropertyName("genre")]
    [XmlAttribute("genre")]
    public string Genre { get; set; }

    [JsonPropertyName("coverArt")]
    [XmlAttribute("coverArt")]
    public string CoverArt { get; set; }

    [JsonPropertyName("size")]
    [XmlAttribute("size")]
    public long Size { get; set; }

    [JsonPropertyName("contentType")]
    [XmlAttribute("contentType")]
    public string ContentType { get; set; }

    [JsonPropertyName("suffix")]
    [XmlAttribute("suffix")]
    public string Suffix { get; set; }
    
    [JsonPropertyName("transcodedContentType")]
    [XmlAttribute("transcodedContentType")]
    public string TranscodedContentType { get; set; }

    [JsonPropertyName("transcodedSuffix")]
    [XmlAttribute("transcodedSuffix")]
    public string transcodedSuffix { get; set; }

    [JsonPropertyName("duration")]
    [XmlAttribute("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("bitRate")]
    [XmlAttribute("bitRate")]
    public int BitRate { get; set; }

    [JsonPropertyName("bitDepth")]
    [XmlAttribute("bitDepth")]
    public int BitDepth { get; set; }

    [JsonPropertyName("samplingRate")]
    [XmlAttribute("samplingRate")]
    public int SamplingRate { get; set; }

    [JsonPropertyName("channelCount")]
    [XmlAttribute("channelCount")]
    public int ChannelCount { get; set; }

    [JsonPropertyName("path")]
    [XmlAttribute("path")]
    public string Path { get; set; }

    [JsonPropertyName("isVideo")]
    [XmlAttribute("isVideo")]
    public bool IsVideo { get; set; }

    [JsonPropertyName("userRating")]
    [XmlAttribute("userRating")]
    public int UserRating { get; set; }

    [JsonPropertyName("averageRating")]
    [XmlAttribute("averageRating")]
    public double AverageRating { get; set; }

    [JsonPropertyName("playCount")]
    [XmlAttribute("playCount")]
    public long PlayCount { get; set; }

    [JsonPropertyName("discNumber")]
    [XmlAttribute("discNumber")]
    public int DiscNumber { get; set; }

    [JsonPropertyName("created")]
    [XmlAttribute("created")]
    public string Created { get; set; }

    [JsonPropertyName("starred")]
    [XmlAttribute("starred")]
    public string Starred { get; set; }

    [JsonPropertyName("albumId")]
    [XmlAttribute("albumId")]
    public string AlbumId { get; set; }

    [JsonPropertyName("artistId")]
    [XmlAttribute("artistId")]
    public string ArtistId { get; set; }

    [JsonPropertyName("type")]
    [XmlAttribute("type")]
    public string Type { get; set; }

    [JsonPropertyName("mediaType")]
    [XmlAttribute("mediaType")]
    public string MediaType { get; set; }

    [JsonPropertyName("bookmarkPosition")]
    [XmlAttribute("bookmarkPosition")]
    public long BookmarkPosition { get; set; }

    [JsonPropertyName("originalWidth")]
    [XmlAttribute("originalWidth")]
    public int OriginalWidth { get; set; }

    [JsonPropertyName("originalHeight")]
    [XmlAttribute("originalHeight")]
    public int OriginalHeight { get; set; }

    [JsonPropertyName("played")]
    [XmlAttribute("played")]
    public string Played { get; set; }

    [JsonPropertyName("bpm")]
    [XmlAttribute("bpm")]
    public int BPM { get; set; }

    [JsonPropertyName("comment")]
    [XmlAttribute("comment")]
    public string Comment { get; set; }

    [JsonPropertyName("sortName")]
    [XmlAttribute("sortName")]
    public string SortName { get; set; }

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

    [JsonPropertyName("albumArtists")]
    [XmlElement("albumArtists")]
    public ArtistID3[] AlbumArtists { get; set; }

    [JsonPropertyName("displayAlbumArtist")]
    [XmlAttribute("displayAlbumArtist")]
    public string DisplayAlbumArtist { get; set; }

    [JsonPropertyName("contributors")]
    [XmlElement("contributors")]
    public Contributor[] Contributors { get; set; }

    [JsonPropertyName("displayComposer")]
    [XmlAttribute("displayComposer")]
    public string DisplayComposer { get; set; }

    [JsonPropertyName("moods")]
    [XmlElement("moods")]
    public string[] Moods { get; set; }

    [JsonPropertyName("replayGain")]
    [XmlElement("replayGain")]
    public ReplayGain ReplayGain { get; set; }
}
