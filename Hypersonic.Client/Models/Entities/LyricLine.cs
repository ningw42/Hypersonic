using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

[XmlRoot("line")]
public class LyricLine
{
    /// <summary>
    /// Line start in milliseconds, or <c>null</c> when the source omits it (unsynced). The single
    /// consumer-facing timing property; both JSON and XML populate it (XML via <see cref="StartXml"/>).
    /// </summary>
    [JsonPropertyName("start")]
    [XmlIgnore]
    public long? Start { get; set; }

    /// <summary>
    /// XML wire shim for <see cref="Start"/>. <see cref="XmlSerializer"/> cannot encode a
    /// <see cref="Nullable{T}"/> value type as an attribute, so the <c>start</c> attribute binds to a
    /// string whose setter funnels into <see cref="Start"/>. Not for consumer use — read <see cref="Start"/>
    /// instead. Ignored by JSON; a missing or non-numeric attribute leaves <see cref="Start"/> null.
    /// </summary>
    [JsonIgnore]
    [XmlAttribute("start")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? StartXml
    {
        get => Start?.ToString(CultureInfo.InvariantCulture);
        set => Start = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    [JsonPropertyName("value")]
    [XmlText]
    public string? Value { get; set; }
}
