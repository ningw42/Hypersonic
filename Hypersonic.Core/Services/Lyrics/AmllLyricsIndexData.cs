using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Pure, immutable parsed view of amll-ttml-db's <c>raw-lyrics-index.jsonl</c> plus the strict
/// title/artist matcher. No HTTP, no disk — the WinUI <c>AmllLyricsIndex</c> supplies the bytes.
/// Keyed by normalized title; each bucket holds every submission (duplicates and title collisions),
/// disambiguated by artist-set intersection at lookup.
/// </summary>
public sealed class AmllLyricsIndexData
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ArtistSeparators =
        new(@"\s*(?:[,/&;、×]|\bfeat\.?|\bft\.?)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<string, List<Entry>> _byTitle;

    private AmllLyricsIndexData(Dictionary<string, List<Entry>> byTitle, int count)
    {
        _byTitle = byTitle;
        Count = count;
    }

    /// <summary>Total number of index entries parsed (across all title buckets).</summary>
    public int Count { get; }

    public static AmllLyricsIndexData Parse(ReadOnlyMemory<byte> jsonl)
    {
        var byTitle = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        var count = 0;

        foreach (var line in Encoding.UTF8.GetString(jsonl.Span).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (TryParseLine(trimmed, out var title, out var entry))
            {
                if (!byTitle.TryGetValue(title, out var bucket))
                {
                    bucket = [];
                    byTitle[title] = bucket;
                }

                bucket.Add(entry);
                count++;
            }
        }

        return new AmllLyricsIndexData(byTitle, count);
    }

    /// <summary>Resolves the best <c>rawLyricFile</c> for a query, or <c>null</c> when strict matching fails.</summary>
    public string? TryResolve(LyricsQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return null;
        }

        if (!_byTitle.TryGetValue(Normalize(query.Title), out var candidates))
        {
            return null;
        }

        var queryArtists = SplitQueryArtists(query.Artist);
        if (queryArtists.Count == 0)
        {
            return null; // strict: no artist to confirm against
        }

        List<Entry>? matches = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Artists.Overlaps(queryArtists))
            {
                (matches ??= []).Add(candidate);
            }
        }

        if (matches is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(query.Album))
        {
            var album = Normalize(query.Album);
            foreach (var match in matches)
            {
                if (match.Album == album)
                {
                    return match.RawLyricFile;
                }
            }
        }

        return matches[0].RawLyricFile;
    }

    private static bool TryParseLine(string line, out string normalizedTitle, out Entry entry)
    {
        normalizedTitle = string.Empty;
        entry = default!;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("rawLyricFile", out var fileEl) || fileEl.GetString() is not { } file)
            {
                return false;
            }

            string? title = null, album = null;
            var artists = new HashSet<string>(StringComparer.Ordinal);

            if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Array)
            {
                foreach (var pair in metadata.EnumerateArray())
                {
                    if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2)
                    {
                        continue;
                    }

                    var key = pair[0].ValueKind == JsonValueKind.String ? pair[0].GetString() : null;
                    var values = pair[1];
                    switch (key)
                    {
                        case "musicName" when values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0:
                            title = values[0].GetString();
                            break;
                        case "album" when values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0:
                            album = values[0].GetString();
                            break;
                        case "artists" when values.ValueKind == JsonValueKind.Array:
                            foreach (var a in values.EnumerateArray())
                            {
                                if (a.GetString() is { } name)
                                {
                                    var normalized = Normalize(name);
                                    if (normalized.Length > 0)
                                    {
                                        artists.Add(normalized);
                                    }
                                }
                            }
                            break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            normalizedTitle = Normalize(title);
            entry = new Entry(file, artists, string.IsNullOrWhiteSpace(album) ? null : Normalize(album));
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false; // skip malformed or wrong-shape lines (bad JSON, or unexpected JSON value kinds)
        }
    }

    internal static string Normalize(string s)
    {
        var normalized = s.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim();
        return WhitespaceRun.Replace(normalized, " ");
    }

    /// <summary>Splits a Subsonic artist string on collab separators into a normalized token set.</summary>
    internal static HashSet<string> SplitQueryArtists(string? artist)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(artist))
        {
            return set;
        }

        foreach (var part in ArtistSeparators.Split(artist))
        {
            var normalized = Normalize(part);
            if (normalized.Length > 0)
            {
                set.Add(normalized);
            }
        }

        return set;
    }

    private sealed record Entry(string RawLyricFile, HashSet<string> Artists, string? Album);
}
