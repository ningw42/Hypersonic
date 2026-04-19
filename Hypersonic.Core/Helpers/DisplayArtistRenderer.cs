using Hypersonic.Core.Models;

namespace Hypersonic.Core.Helpers;

public enum ArtistSegmentKind
{
    Connector,
    Artist,
}

public class ArtistSegment
{
    public ArtistSegmentKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;
    public SongArtistInfo? Artist { get; init; }
}

public record BuildSegmentsResult(
    List<ArtistSegment> Segments,
    bool UsedStructuredFallback,
    bool HasUnmatchedArtists);

public static class DisplayArtistRenderer
{
    public static BuildSegmentsResult BuildSegments(
        string? displayArtist,
        List<SongArtistInfo> artists,
        bool hasStructuredArtists)
    {
        if (artists.Count == 0)
        {
            if (!string.IsNullOrEmpty(displayArtist))
            {
                return new BuildSegmentsResult(
                    [new ArtistSegment { Kind = ArtistSegmentKind.Artist, Text = displayArtist, Artist = new SongArtistInfo { Name = displayArtist } }],
                    false, false);
            }
            return new BuildSegmentsResult([], false, false);
        }

        var nonEmptyArtists = artists.Where(a => !string.IsNullOrEmpty(a.Name)).ToList();

        if (string.IsNullOrEmpty(displayArtist))
        {
            if (nonEmptyArtists.Count == 0)
            {
                return new BuildSegmentsResult([], false, false);
            }
            if (nonEmptyArtists.Count == 1)
            {
                return new BuildSegmentsResult(
                    [new ArtistSegment { Kind = ArtistSegmentKind.Artist, Text = nonEmptyArtists[0].Name, Artist = nonEmptyArtists[0] }],
                    false, false);
            }
            return BuildCommaSeparated(nonEmptyArtists);
        }
        if (nonEmptyArtists.Count == 0)
        {
            return new BuildSegmentsResult(
                [new ArtistSegment { Kind = ArtistSegmentKind.Artist, Text = displayArtist, Artist = new SongArtistInfo { Name = displayArtist } }],
                false, false);
        }

        var matches = FindMatches(displayArtist, nonEmptyArtists);

        if (matches.Count == 0)
        {
            if (hasStructuredArtists)
            {
                var result = BuildCommaSeparated(nonEmptyArtists);
                return new BuildSegmentsResult(result.Segments, true, false);
            }
            return new BuildSegmentsResult(
                [new ArtistSegment { Kind = ArtistSegmentKind.Artist, Text = displayArtist, Artist = new SongArtistInfo { Name = displayArtist } }],
                false, false);
        }

        var hasUnmatched = matches.Count < nonEmptyArtists.Count;
        var segments = BuildSegmentsFromMatches(displayArtist, matches);
        return new BuildSegmentsResult(segments, false, hasUnmatched);
    }

    private static List<(int Start, int Length, SongArtistInfo Artist)> FindMatches(
        string displayArtist, List<SongArtistInfo> artists)
    {
        var sorted = artists
            .Where(a => !string.IsNullOrEmpty(a.Name))
            .OrderByDescending(a => a.Name.Length)
            .ToList();

        var matches = new List<(int Start, int Length, SongArtistInfo Artist)>();
        var occupied = new bool[displayArtist.Length];

        foreach (var artist in sorted)
        {
            var searchFrom = 0;
            while (searchFrom < displayArtist.Length)
            {
                var idx = displayArtist.IndexOf(artist.Name, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    break;
                }

                var end = idx + artist.Name.Length;

                if (!IsBoundaryMatch(displayArtist, idx, end))
                {
                    searchFrom = idx + 1;
                    continue;
                }

                var overlaps = false;
                for (var i = idx; i < end; i++)
                {
                    if (occupied[i])
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (overlaps)
                {
                    searchFrom = idx + 1;
                    continue;
                }

                for (var i = idx; i < end; i++)
                {
                    occupied[i] = true;
                }
                matches.Add((idx, artist.Name.Length, artist));
                break;
            }
        }

        matches.Sort((a, b) => a.Start.CompareTo(b.Start));
        return matches;
    }

    private static bool IsBoundaryMatch(string text, int start, int end)
    {
        if (start > 0 && char.IsLetterOrDigit(text[start - 1]))
        {
            return false;
        }
        if (end < text.Length && char.IsLetterOrDigit(text[end]))
        {
            return false;
        }
        return true;
    }

    private static List<ArtistSegment> BuildSegmentsFromMatches(
        string displayArtist, List<(int Start, int Length, SongArtistInfo Artist)> matches)
    {
        var segments = new List<ArtistSegment>();
        var pos = 0;

        foreach (var (start, length, artist) in matches)
        {
            if (start > pos)
            {
                segments.Add(new ArtistSegment
                {
                    Kind = ArtistSegmentKind.Connector,
                    Text = displayArtist[pos..start],
                });
            }
            segments.Add(new ArtistSegment
            {
                Kind = ArtistSegmentKind.Artist,
                Text = displayArtist.Substring(start, length),
                Artist = artist,
            });
            pos = start + length;
        }

        if (pos < displayArtist.Length)
        {
            segments.Add(new ArtistSegment
            {
                Kind = ArtistSegmentKind.Connector,
                Text = displayArtist[pos..],
            });
        }

        return segments;
    }

    private static BuildSegmentsResult BuildCommaSeparated(List<SongArtistInfo> artists)
    {
        var segments = new List<ArtistSegment>();
        for (var i = 0; i < artists.Count; i++)
        {
            if (i > 0)
            {
                segments.Add(new ArtistSegment { Kind = ArtistSegmentKind.Connector, Text = ", " });
            }
            segments.Add(new ArtistSegment
            {
                Kind = ArtistSegmentKind.Artist,
                Text = artists[i].Name,
                Artist = artists[i],
            });
        }
        return new BuildSegmentsResult(segments, false, false);
    }
}
