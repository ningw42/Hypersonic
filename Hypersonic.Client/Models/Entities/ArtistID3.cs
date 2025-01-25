using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

/// <summary>
/// This is an alias of Artist, for reason, check out <see cref="Artist"/>
/// </summary>
/// <seealso cref="https://opensubsonic.netlify.app/docs/responses/artistid3/"/>
[XmlRoot("artist")]
public class ArtistID3 : Artist
{
}

