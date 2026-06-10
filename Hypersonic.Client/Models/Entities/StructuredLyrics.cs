using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

[XmlRoot("structuredLyrics")]
public class StructuredLyrics
{
    [JsonPropertyName("displayArtist")]
    [XmlAttribute("displayArtist")]
    public string? DisplayArtist { get; set; }

    [JsonPropertyName("displayTitle")]
    [XmlAttribute("displayTitle")]
    public string? DisplayTitle { get; set; }

    [JsonPropertyName("lang")]
    [XmlAttribute("lang")]
    public string? Lang { get; set; }

    /// <summary>
    /// Global timing offset in milliseconds, or <c>null</c> when the source omits it. The single
    /// consumer-facing offset property; both JSON and XML populate it (XML via <see cref="OffsetXml"/>).
    /// </summary>
    [JsonPropertyName("offset")]
    [XmlIgnore]
    public long? Offset { get; set; }

    /// <summary>
    /// XML wire shim for <see cref="Offset"/>. <see cref="XmlSerializer"/> cannot encode a
    /// <see cref="Nullable{T}"/> value type as an attribute, so the <c>offset</c> attribute binds to a
    /// string whose setter funnels into <see cref="Offset"/>. Not for consumer use — read <see cref="Offset"/>
    /// instead. Ignored by JSON; a missing or non-numeric attribute leaves <see cref="Offset"/> null.
    /// </summary>
    [JsonIgnore]
    [XmlAttribute("offset")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? OffsetXml
    {
        get => Offset?.ToString(CultureInfo.InvariantCulture);
        set => Offset = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    [JsonPropertyName("synced")]
    [XmlAttribute("synced")]
    public bool Synced { get; set; }

    [JsonPropertyName("line")]
    [XmlElement("line")]
    public LyricLine[]? Line { get; set; }
}
