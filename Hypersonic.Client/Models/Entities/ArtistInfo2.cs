namespace Hypersonic.Client.Models.Entities;

/// <summary>
/// The only difference between ArtistInfo and ArtistInfo2 is the 'similarArtist' property,
/// ArtistInfo is Artist while ArtistInfo2 is ArtistID3.
/// Given we have made the decision to merge Artist and ArtistID3 in the implementation,
/// we just make ArtistInfo2 an alias of ArtistInfo
/// </summary>
/// <seealso cref="https://opensubsonic.netlify.app/docs/responses/artistinfo2/"/>
public class ArtistInfo2 : ArtistInfo
{
}
