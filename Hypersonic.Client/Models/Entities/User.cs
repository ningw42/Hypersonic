using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class User
{
    [JsonPropertyName("username")]
    [XmlAttribute("username")]
    public string Username { get; set; } = null!;

    [JsonPropertyName("email")]
    [XmlAttribute("email")]
    public string? Email { get; set; }

    [JsonPropertyName("scrobblingEnabled")]
    [XmlAttribute("scrobblingEnabled")]
    public bool ScrobblingEnabled { get; set; }

    [JsonPropertyName("adminRole")]
    [XmlAttribute("adminRole")]
    public bool AdminRole { get; set; }

    [JsonPropertyName("settingsRole")]
    [XmlAttribute("settingsRole")]
    public bool SettingsRole { get; set; }

    [JsonPropertyName("downloadRole")]
    [XmlAttribute("downloadRole")]
    public bool DownloadRole { get; set; }

    [JsonPropertyName("uploadRole")]
    [XmlAttribute("uploadRole")]
    public bool UploadRole { get; set; }

    [JsonPropertyName("playlistRole")]
    [XmlAttribute("playlistRole")]
    public bool PlaylistRole { get; set; }

    [JsonPropertyName("coverArtRole")]
    [XmlAttribute("coverArtRole")]
    public bool CoverArtRole { get; set; }

    [JsonPropertyName("commentRole")]
    [XmlAttribute("commentRole")]
    public bool CommentRole { get; set; }

    [JsonPropertyName("podcastRole")]
    [XmlAttribute("podcastRole")]
    public bool PodcastRole { get; set; }

    [JsonPropertyName("streamRole")]
    [XmlAttribute("streamRole")]
    public bool StreamRole { get; set; }

    [JsonPropertyName("jukeboxRole")]
    [XmlAttribute("jukeboxRole")]
    public bool JukeboxRole { get; set; }

    [JsonPropertyName("shareRole")]
    [XmlAttribute("shareRole")]
    public bool ShareRole { get; set; }

    [JsonPropertyName("videoConversionRole")]
    [XmlAttribute("videoConversionRole")]
    public bool VideoConversionRole { get; set; }

    [JsonPropertyName("avatarLastChanged")]
    [XmlAttribute("avatarLastChanged")]
    public DateTime? AvatarLastChanged { get; set; }

    [JsonPropertyName("folder")]
    [XmlElement("folder")]
    public int[]? Folder { get; set; }
}
