using System.Globalization;
using System.Text.RegularExpressions;

namespace Hypersonic.Core.Services.Lyrics;

public readonly record struct LrcTimedLine(int TimeMs, string Text);

public readonly record struct LrcParseResult(IReadOnlyList<LrcTimedLine> Lines, int? OffsetMs);

/// <summary>
/// Reusable tokenizer for LRC-shaped lyric text. Emits one <see cref="LrcTimedLine"/> per
/// timestamp (multi-timestamp lines repeat), honors <c>[offset:±ms]</c>, ignores informational
/// metadata tags, and returns lines sorted ascending by time. Format-agnostic — building a
/// <see cref="Hypersonic.Core.Models.Lyrics.LyricsDocument"/> and validating it belong to the parser.
/// </summary>
public static partial class LrcLineParser
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // [mm:ss.xx] / [mm:ss.xxx] / [mm:ss] — minutes, seconds, optional fractional digits.
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant)]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    public static LrcParseResult Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new LrcParseResult(Array.Empty<LrcTimedLine>(), null);
        }

        int? offsetMs = null;
        var lines = new List<LrcTimedLine>();

        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var offsetMatch = OffsetTag().Match(raw);
            if (offsetMatch.Success
                && int.TryParse(offsetMatch.Groups[1].Value, NumberStyles.AllowLeadingSign, Invariant, out var off))
            {
                offsetMs = off;
                continue;
            }

            var matches = TimeTag().Matches(raw);
            if (matches.Count == 0)
            {
                continue; // untimed / informational tag line — dropped
            }

            // Text is everything after the LAST timestamp on the line.
            var lastTag = matches[^1];
            var lineText = raw[(lastTag.Index + lastTag.Length)..].Trim();

            foreach (Match m in matches)
            {
                if (TryToMs(m, out var ms))
                {
                    lines.Add(new LrcTimedLine(ms, lineText));
                }
            }
        }

        // OrderBy is a stable sort — same-time lines keep source order (e.g. duet/overlap markers).
        return new LrcParseResult(lines.OrderBy(static l => l.TimeMs).ToList(), offsetMs);
    }

    private static bool TryToMs(Match m, out int ms)
    {
        ms = 0;
        if (!int.TryParse(m.Groups[1].Value, out var min) || !int.TryParse(m.Groups[2].Value, out var sec))
        {
            return false;
        }

        var frac = 0;
        if (m.Groups[3].Success)
        {
            var digits = m.Groups[3].Value;
            if (!int.TryParse(digits, out frac))
            {
                return false;
            }

            // Normalize hundredths vs milliseconds: 1 digit → *100, 2 → *10, 3 → as-is.
            frac = digits.Length switch { 1 => frac * 100, 2 => frac * 10, _ => frac };
        }

        var total = ((long)min * 60 + sec) * 1000 + frac;
        if (total < 0 || total > int.MaxValue)
        {
            return false;
        }

        ms = (int)total;
        return true;
    }
}
