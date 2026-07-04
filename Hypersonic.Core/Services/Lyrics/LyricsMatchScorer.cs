using System.Text;
using System.Text.RegularExpressions;

using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Pure, weighted match scorer for a search candidate against a query. Combines normalized-Levenshtein
/// title/album/artist similarity with duration proximity into a 0–100 score. A factor whose data is
/// absent on either side is dropped and the remaining weights renormalized (missing = neutral). Used
/// for both candidate selection (max score) and confidence gating (score ≥ threshold).
/// </summary>
public static class LyricsMatchScorer
{
    private const double TitleWeight = 0.35;
    private const double ArtistWeight = 0.30;
    private const double DurationWeight = 0.30;
    private const double AlbumWeight = 0.05;
    private const int DurationToleranceSeconds = 12;

    private static readonly char[] ArtistSeparators = ['/', ',', ';', '&', '、', '，'];
    private static readonly string[] ArtistWordDelimiters =
        [" feat.", " feat ", " ft.", " ft ", " featuring ", " vs.", " vs "];

    public static int Score(LyricsQuery query, LyricsCandidate candidate)
    {
        double acc = 0;
        double weightSum = 0;

        // Title — always present (blank query title is rejected upstream).
        AddFactor(ref acc, ref weightSum, TitleWeight,
            Similarity(StripQualifier(Normalize(query.Title)), StripQualifier(Normalize(candidate.Title))));

        if (!string.IsNullOrWhiteSpace(query.Album) && !string.IsNullOrWhiteSpace(candidate.Album))
        {
            AddFactor(ref acc, ref weightSum, AlbumWeight,
                Similarity(StripQualifier(Normalize(query.Album)), StripQualifier(Normalize(candidate.Album))));
        }

        if (query.DurationSeconds is int qd && candidate.DurationSeconds is int cd)
        {
            var delta = Math.Abs(cd - qd);
            var sim = Math.Max(0.0, 1.0 - (double)delta / DurationToleranceSeconds);
            AddFactor(ref acc, ref weightSum, DurationWeight, sim);
        }

        var queryTokens = ArtistTokens(query.Artist);
        var candTokens = ArtistTokens(candidate.Artist);
        if (queryTokens.Count > 0 && candTokens.Count > 0)
        {
            double sum = 0;
            foreach (var q in queryTokens)
            {
                double best = 0;
                foreach (var c in candTokens)
                {
                    best = Math.Max(best, Similarity(q, c));
                }
                sum += best;
            }
            AddFactor(ref acc, ref weightSum, ArtistWeight, sum / queryTokens.Count);
        }

        return weightSum <= 0 ? 0 : (int)Math.Round(100.0 * acc / weightSum);
    }

    private static void AddFactor(ref double acc, ref double weightSum, double weight, double factor)
    {
        acc += weight * factor;
        weightSum += weight;
    }

    internal static double Similarity(string a, string b)
    {
        var max = Math.Max(a.Length, b.Length);
        return max == 0 ? 1.0 : 1.0 - (double)Levenshtein(a, b) / max;
    }

    internal static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var collapsed = Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
        var sb = new StringBuilder(collapsed.Length);
        foreach (var ch in collapsed)
        {
            if (ch >= '！' && ch <= '～') sb.Append((char)(ch - 0xFEE0)); // full-width → half-width
            else if (ch == '　') sb.Append(' ');                              // ideographic space
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string StripQualifier(string normalized)
        => Regex.Replace(normalized, @"\s*[\(\[].*?[\)\]]\s*$", string.Empty).Trim();

    internal static List<string> ArtistTokens(string? artist)
    {
        var normalized = Normalize(artist);
        if (normalized.Length == 0) return [];

        foreach (var delimiter in ArtistWordDelimiters)
        {
            normalized = normalized.Replace(delimiter, "/", StringComparison.Ordinal);
        }

        return normalized
            .Split(ArtistSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
