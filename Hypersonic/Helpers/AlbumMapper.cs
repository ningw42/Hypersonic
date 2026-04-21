using Hypersonic.Client;
using Hypersonic.Client.Models.Entities;
using Hypersonic.Core.Helpers;

using CoreAlbumInfo = Hypersonic.Core.Models.AlbumInfo;

namespace Hypersonic.Helpers;

public static class AlbumMapper
{
    public static CoreAlbumInfo Map(AlbumID3 album, ISubsonicClient client)
    {
        var (artists, hasStructured) = SongArtistMapper.MapArtists(
            album.Artists?.Select(a => (a.Name, (string?)a.Id)),
            album.Artist,
            album.ArtistId);

        return new CoreAlbumInfo
        {
            Id = album.Id,
            Name = album.Name ?? string.Empty,
            Artist = album.DisplayArtist ?? album.Artist ?? string.Empty,
            ArtistId = album.ArtistId,
            DisplayArtist = album.DisplayArtist,
            Artists = artists,
            HasStructuredArtists = hasStructured,
            Year = album.Year,
            CoverArt = album.CoverArt,
            CoverArtUrl = !string.IsNullOrEmpty(album.CoverArt)
                ? client.GetCoverArtUri(album.CoverArt, "300").ToString()
                : null,
            SongCount = album.SongCount,
            Duration = TimeSpan.FromSeconds(album.Duration),
        };
    }

    public static List<CoreAlbumInfo> MapAll(AlbumID3[]? albums, ISubsonicClient client)
    {
        if (albums == null || albums.Length == 0)
        {
            return [];
        }

        return albums.Select(a => Map(a, client)).ToList();
    }
}
