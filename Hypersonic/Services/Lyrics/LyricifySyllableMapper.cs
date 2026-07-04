using Hypersonic.Core.Models.Lyrics;

using Lyricify.Lyrics.Models;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Maps the library's parsed line/syllable model (<see cref="ILineInfo"/> / <see cref="SyllableLineInfo"/>)
/// into a word-tier <see cref="LyricsDocument"/>. Mirrors <c>TtmlLyricsParser</c>: normalizes word
/// timings (monotonic starts, end ≥ start) and sets each line's bounds to enclose all its words so the
/// validator's word-in-bounds invariant holds. A line with no syllables gets one synthetic word.
/// </summary>
internal static class LyricifySyllableMapper
{
    public static LyricsDocument? ToWordDocument(IReadOnlyList<ILineInfo> lines, string sourceId)
    {
        var outLines = new List<LyricsLine>(lines.Count);

        foreach (var line in lines)
        {
            var text = line.Text ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            var words = new List<LyricsWord>();
            if (line is SyllableLineInfo syllabic && syllabic.Syllables is { Count: > 0 } syllables)
            {
                foreach (var s in syllables)
                {
                    // SyllableInfo.StartTime/EndTime are int ms in the library — no cast needed.
                    words.Add(new LyricsWord { StartMs = s.StartTime, EndMs = Math.Max(s.EndTime, s.StartTime), Text = s.Text ?? string.Empty });
                }
            }
            else
            {
                var start = line.StartTime ?? 0;
                words.Add(new LyricsWord { StartMs = start, EndMs = start, Text = text });
            }

            words = NormalizeWordTimings(words);

            var minStart = int.MaxValue;
            var maxEnd = int.MinValue;
            foreach (var word in words)
            {
                minStart = Math.Min(minStart, word.StartMs);
                maxEnd = Math.Max(maxEnd, word.EndMs);
            }

            outLines.Add(new LyricsLine
            {
                StartMs = Math.Max(minStart, 0),
                EndMs = maxEnd,
                Text = text,
                Words = words,
            });
        }

        if (outLines.Count == 0)
        {
            return null;
        }

        return new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            Lines = outLines,
            SourceProvider = sourceId,
        };
    }

    private static List<LyricsWord> NormalizeWordTimings(List<LyricsWord> words)
    {
        var normalized = new List<LyricsWord>(words.Count);
        var previousStart = int.MinValue;

        foreach (var word in words)
        {
            var start = Math.Max(word.StartMs, previousStart);
            var end = Math.Max(word.EndMs, start);
            normalized.Add(new LyricsWord { StartMs = start, EndMs = end, Text = word.Text });
            previousStart = start;
        }

        return normalized;
    }
}
