namespace Hypersonic.Client.Models.Entities;

/// <summary>
/// There is no spec for Song, but the 'song' property in AlbumID3WithSong is a Child,
/// so, we are making an alias to Child
/// </summary>
/// <seealso cref="https://opensubsonic.netlify.app/docs/responses/song/"/>
public class Song : Child
{
}
