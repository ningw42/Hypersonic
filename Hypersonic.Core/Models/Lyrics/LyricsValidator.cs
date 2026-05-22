namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// Pure-function validator for <see cref="LyricsDocument"/>. Returns the list
/// of invariants the document violates; an empty list means the document is
/// safe for the renderer to consume. Never throws.
/// </summary>
public static class LyricsValidator
{
    public static IReadOnlyList<string> Validate(LyricsDocument document)
    {
        var issues = new List<string>();
        int? previousLineStart = null;

        for (var i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];
            ValidateLineTimings(issues, i, line, ref previousLineStart);
            ValidateWordsWithinLine(issues, i, line);
            ValidateSyncKindForLine(issues, i, line, document.SyncKind);
        }

        if (document.SyncKind == LyricsSyncKind.None && document.Lines.Count > 0)
        {
            issues.Add($"SyncKind=None requires zero lines, but document has {document.Lines.Count}.");
        }

        return issues;
    }

    private static void ValidateLineTimings(List<string> issues, int i, LyricsLine line, ref int? previousLineStart)
    {
        if (line.StartMs < 0)
        {
            issues.Add($"Line {i}: StartMs is negative ({line.StartMs}).");
        }

        if (line.EndMs.HasValue && line.EndMs.Value < line.StartMs)
        {
            issues.Add($"Line {i}: EndMs ({line.EndMs.Value}) is before StartMs ({line.StartMs}).");
        }

        if (previousLineStart.HasValue && line.StartMs < previousLineStart.Value)
        {
            issues.Add($"Line {i}: lines are not monotonic by StartMs ({line.StartMs} < {previousLineStart.Value}).");
        }
        previousLineStart = line.StartMs;
    }

    private static void ValidateWordsWithinLine(List<string> issues, int i, LyricsLine line)
    {
        int? previousWordStart = null;
        for (var w = 0; w < line.Words.Count; w++)
        {
            var word = line.Words[w];

            if (word.EndMs < word.StartMs)
            {
                issues.Add($"Line {i}, Word {w}: EndMs ({word.EndMs}) is before StartMs ({word.StartMs}).");
            }

            if (previousWordStart.HasValue && word.StartMs < previousWordStart.Value)
            {
                issues.Add($"Line {i}, Word {w}: words are not monotonic by StartMs.");
            }
            previousWordStart = word.StartMs;

            if (line.EndMs.HasValue
                && (word.StartMs < line.StartMs || word.EndMs > line.EndMs.Value))
            {
                issues.Add($"Line {i}, Word {w}: timing [{word.StartMs}, {word.EndMs}] falls outside line bounds [{line.StartMs}, {line.EndMs.Value}].");
            }
        }
    }

    private static void ValidateSyncKindForLine(List<string> issues, int i, LyricsLine line, LyricsSyncKind kind)
    {
        switch (kind)
        {
            case LyricsSyncKind.Word:
                if (line.Words.Count == 0)
                {
                    issues.Add($"Line {i}: SyncKind=Word requires at least one word.");
                }
                break;

            case LyricsSyncKind.Line:
                if (line.Words.Count > 0)
                {
                    issues.Add($"Line {i}: SyncKind=Line must not carry word-level timings.");
                }
                break;

            case LyricsSyncKind.Plain:
                if (line.StartMs != 0)
                {
                    issues.Add($"Line {i}: SyncKind=Plain requires StartMs=0 (got {line.StartMs}).");
                }
                if (line.Words.Count > 0)
                {
                    issues.Add($"Line {i}: SyncKind=Plain must not carry word-level timings.");
                }
                break;
        }
    }
}
