using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Hypersonic.Client.Models.Entities;

public class SimilarSongs : Songs
{
}

public class SimilarSongs2 : Songs
{
}

public class TopSongs : Songs
{
}

public class SongByGenre : Songs
{
}

public class RandomSongs : Songs
{
}

public class Starred : Songs
{
}

public class Starred2 : Songs
{
}

public class Songs
{
    [JsonPropertyName("song")]
    [XmlElement("song")]
    public Song[]? Song { get; set; }
}
