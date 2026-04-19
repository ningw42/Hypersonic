using Hypersonic.Core.Models;

namespace Hypersonic.Core.Helpers;

public static class SongArtistMapper
{
    public static (List<SongArtistInfo> Artists, bool HasStructured) MapArtists(
        IEnumerable<(string Name, string? Id)>? artists,
        string? fallbackName,
        string? fallbackId)
    {
        if (artists != null)
        {
            var list = artists
                .Where(a => !string.IsNullOrEmpty(a.Name))
                .Select(a => new SongArtistInfo { Name = a.Name, Id = a.Id })
                .ToList();
            if (list.Count > 0)
            {
                return (list, true);
            }
        }
        return ([new SongArtistInfo { Name = fallbackName ?? string.Empty, Id = fallbackId }], false);
    }
}
