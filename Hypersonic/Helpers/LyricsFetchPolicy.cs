namespace Hypersonic.Helpers;

using Hypersonic.Client.Models.Entities;

/// <summary>
/// Per-call classification of a lyrics endpoint outcome (structured or legacy).
/// </summary>
public enum LyricsOutcomeKind
{
    /// <summary>HTTP success + Status="ok" + non-blank lyrics.</summary>
    Content,
    /// <summary>Definitive empty: server says "no lyrics for this track".</summary>
    DefinitiveEmpty,
    /// <summary>Definitive unsupported: endpoint exists but server can't service it.</summary>
    DefinitiveUnsupported,
    /// <summary>Transient: throw, network failure, non-definitive Status="failed" code.</summary>
    Transient,
}

/// <summary>
/// Result of a single lyrics fetch (structured or legacy).
/// Text is non-null only when <see cref="Kind"/> is <see cref="LyricsOutcomeKind.Content"/>.
/// </summary>
public sealed record LyricsOutcome(LyricsOutcomeKind Kind, string? Text)
{
    public static LyricsOutcome Content(string text) => new(LyricsOutcomeKind.Content, text);
    public static readonly LyricsOutcome DefinitiveEmpty = new(LyricsOutcomeKind.DefinitiveEmpty, null);
    public static readonly LyricsOutcome DefinitiveUnsupported = new(LyricsOutcomeKind.DefinitiveUnsupported, null);
    public static readonly LyricsOutcome Transient = new(LyricsOutcomeKind.Transient, null);

    public bool IsDefinitive => Kind != LyricsOutcomeKind.Transient;
}

/// <summary>
/// Two-call result with the resolved display state and whether the result
/// should be cached. Cache write requires both sides to be definitive.
/// </summary>
public sealed record LyricsFetchResult(
    string? Text,
    bool ShouldCache);

/// <summary>
/// Pure decision logic for combining the structured + legacy outcomes into a
/// final display + cache-write decision (per the cache-write matrix in the
/// Now Playing design doc).
/// </summary>
public static class LyricsFetchPolicy
{
    /// <summary>
    /// True iff Subsonic error code <paramref name="code"/> is one of the
    /// "definitive" failure codes (20 protocol mismatch, 30 incompatible
    /// client, 70 data not found). All other codes are transient.
    /// </summary>
    public static bool IsDefinitiveErrorCode(int code) => code is 20 or 30 or 70;

    /// <summary>
    /// True iff <paramref name="code"/> represents a "data not found" /
    /// "no content" classification (code 70) vs an "endpoint not available"
    /// classification (code 20 or 30). Transient codes are not handled here.
    /// </summary>
    public static bool IsEmptyClassificationCode(int code) => code == 70;

    /// <summary>
    /// Maps a Subsonic <c>Status="failed"</c> response code to a per-call
    /// <see cref="LyricsOutcome"/>: definitive codes (20/30/70) are
    /// classified as definitive empty (70) or unsupported (20/30); any
    /// other code is transient.
    /// </summary>
    public static LyricsOutcome ClassifyFailedStatus(int code)
    {
        if (!IsDefinitiveErrorCode(code))
        {
            return LyricsOutcome.Transient;
        }
        return IsEmptyClassificationCode(code)
            ? LyricsOutcome.DefinitiveEmpty
            : LyricsOutcome.DefinitiveUnsupported;
    }

    public static LyricsFetchResult Combine(LyricsOutcome structured, LyricsOutcome legacy)
    {
        if (structured.Kind == LyricsOutcomeKind.Content)
        {
            // Short-circuit — legacy isn't called when structured returns content.
            return new LyricsFetchResult(structured.Text, ShouldCache: true);
        }

        if (legacy.Kind == LyricsOutcomeKind.Content)
        {
            // Cache the legacy text only when structured was definitive (we
            // know structured wouldn't have produced a different answer).
            return new LyricsFetchResult(legacy.Text, ShouldCache: structured.IsDefinitive);
        }

        // Both sides ended without content. Cache the null only when BOTH were
        // definitive. Any transient outcome blocks the cache write.
        var bothDefinitive = structured.IsDefinitive && legacy.IsDefinitive;
        return new LyricsFetchResult(null, ShouldCache: bothDefinitive);
    }

    /// <summary>
    /// Flatten the first usable <see cref="StructuredLyrics"/> entry to plain
    /// text, joining non-blank line values with newlines. Returns
    /// <c>null</c> when no entry is usable (per the "Usable entry" definition
    /// in the design doc: <c>Line</c> non-null and at least one
    /// <c>LyricLine.Value</c> non-blank).
    /// </summary>
    public static string? FlattenStructured(StructuredLyrics[]? entries)
    {
        if (entries == null || entries.Length == 0)
        {
            return null;
        }

        foreach (var entry in entries)
        {
            if (entry?.Line == null)
            {
                continue;
            }

            var lines = entry.Line
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Value))
                .Select(l => l.Value!)
                .ToList();
            if (lines.Count > 0)
            {
                return string.Join("\n", lines);
            }
        }

        return null;
    }
}
