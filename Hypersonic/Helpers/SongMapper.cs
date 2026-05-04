using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;
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
            CoverImage = ImageReference.SubsonicCoverArt(song.CoverArt),
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
            PlayCount = song.PlayCount,
            BPM = song.BPM,
            IsStarred = !string.IsNullOrEmpty(song.Starred),
        };
    }
}
