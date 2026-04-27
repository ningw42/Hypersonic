using Hypersonic.Core.Helpers;
using Hypersonic.Models;

namespace Hypersonic.Helpers;

public static class SongMapper
{
    public static SongInfo MapFromChild(Client.Models.Entities.Child song, Client.ISubsonicClient client, string? albumVersion = null)
    {
        var (artists, hasStructured) = SongArtistMapper.MapArtists(
            song.Artists?.Select(a => (a.Name, (string?)a.Id)),
            song.Artist,
            song.ArtistId);

        return new SongInfo
        {
            Id = song.Id,
            TrackNumber = song.Track,
            Title = song.Title ?? string.Empty,
            Duration = song.Duration,
            DiscNumber = song.DiscNumber,
            Artist = song.Artist ?? string.Empty,
            Artists = artists,
            HasStructuredArtists = hasStructured,
            Album = albumVersion != null ? AlbumMapper.StripVersionSuffix(song.Album, albumVersion) : song.Album ?? string.Empty,
            Genre = song.Genre,
            Year = song.Year,
            CoverArt = song.CoverArt,
            CoverArtUrl = !string.IsNullOrEmpty(song.CoverArt)
                ? client.GetCoverArtUri(song.CoverArt, "300").ToString()
                : null,
            BitRate = song.BitRate,
            BitDepth = song.BitDepth,
            SamplingRate = song.SamplingRate,
            Suffix = song.Suffix,
            Size = song.Size,
            Path = song.Path,
            AlbumId = song.AlbumId,
            ArtistId = song.ArtistId,
            DisplayArtist = song.DisplayArtist,
            StreamUri = client.GetStreamUri(song.Id),
            CoverArtUri = !string.IsNullOrEmpty(song.CoverArt)
                ? client.GetCoverArtUri(song.CoverArt, "300")
                : null,
            PlayCount = song.PlayCount,
            BPM = song.BPM,
            IsStarred = !string.IsNullOrEmpty(song.Starred),
        };
    }
}
