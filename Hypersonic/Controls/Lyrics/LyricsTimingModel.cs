using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Controls.Lyrics;

/// <summary>One timed word inside a <see cref="LyricsTimingLine"/> (offset already folded in).</summary>
internal sealed class LyricsTimingWord
{
    public int StartMs { get; init; }
    public int EndMs { get; init; }
    public string Text { get; init; } = string.Empty;

    public int DurationMs => Math.Max(EndMs - StartMs, 0);
}

/// <summary>
/// One line of the resolved timing model: its (offset-folded) start, its <i>resolved</i> end, the text,
/// and an ordered word list that always has at least one entry (a synthetic line-spanning word for a
/// line-only source). Static documents (Plain) leave <see cref="Words"/> empty.
/// </summary>
internal sealed class LyricsTimingLine
{
    public int StartMs { get; init; }
    public int EndMs { get; init; }
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<LyricsTimingWord> Words { get; init; } = Array.Empty<LyricsTimingWord>();

    /// <summary>True when <see cref="Words"/> are real per-word timings (eligible for bloom); false for
    /// a synthesized single line-spanning word.</summary>
    public bool HasWordTiming { get; init; }
}

/// <summary>
/// The platform-independent timing core the renderer draws against: a <see cref="LyricsDocument"/>
/// turned into lines with resolved ends, folded global offset, and synthetic words for line-only
/// sources. Pure and fully unit-tested; the Win2D visual model layers text layout on top of it. See
/// the Phase 2 design doc §3.3.
/// </summary>
internal sealed class LyricsTimingModel
{
    /// <summary>Plain / None — no wipe or scroll-sync; the renderer draws all lines statically.</summary>
    public bool IsStatic { get; private init; }

    public IReadOnlyList<LyricsTimingLine> Lines { get; private init; } = Array.Empty<LyricsTimingLine>();

    public static readonly LyricsTimingModel Empty = new();

    /// <summary>
    /// Builds the timing model. <paramref name="trackDurationMs"/> bounds the final line's end when it
    /// has no explicit end; <paramref name="defaultTailMs"/> is the last-resort tail when neither an end
    /// nor a duration is known.
    /// </summary>
    public static LyricsTimingModel Build(LyricsDocument? document, double? trackDurationMs, int defaultTailMs = 4000)
    {
        if (document is null || document.Lines.Count == 0 || document.SyncKind == LyricsSyncKind.None)
        {
            return Empty;
        }

        if (document.SyncKind == LyricsSyncKind.Plain)
        {
            var staticLines = document.Lines
                .Select(l => new LyricsTimingLine { StartMs = 0, EndMs = 0, Text = l.Text })
                .ToList();
            return new LyricsTimingModel { IsStatic = true, Lines = staticLines };
        }

        var offset = document.OffsetMs ?? 0;
        var src = document.Lines;
        var lines = new List<LyricsTimingLine>(src.Count);

        for (var i = 0; i < src.Count; i++)
        {
            var line = src[i];
            var start = line.StartMs + offset;

            int end;
            if (line.EndMs.HasValue)
            {
                end = line.EndMs.Value + offset;
            }
            else if (i + 1 < src.Count)
            {
                end = src[i + 1].StartMs + offset;
            }
            else
            {
                end = trackDurationMs is { } dur && dur > int.MinValue && dur < int.MaxValue
                    ? (int)dur
                    : start + defaultTailMs;
            }

            end = Math.Max(end, start);

            IReadOnlyList<LyricsTimingWord> words;
            bool hasWordTiming;
            if (line.Words.Count > 0)
            {
                words = line.Words
                    .Select(w => new LyricsTimingWord { StartMs = w.StartMs + offset, EndMs = w.EndMs + offset, Text = w.Text })
                    .ToList();
                hasWordTiming = true;
            }
            else
            {
                // Line-only source: one synthetic word spanning the resolved line, so the renderer can
                // run the same per-row wipe (a clean line-level fill) with no per-word bloom.
                words = new[] { new LyricsTimingWord { StartMs = start, EndMs = end, Text = line.Text } };
                hasWordTiming = false;
            }

            lines.Add(new LyricsTimingLine
            {
                StartMs = start,
                EndMs = end,
                Text = line.Text,
                Words = words,
                HasWordTiming = hasWordTiming,
            });
        }

        return new LyricsTimingModel { IsStatic = false, Lines = lines };
    }
}
