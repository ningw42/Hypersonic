using System.Globalization;
using System.Text;
using System.Xml.Linq;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Parses an AMLL-flavoured TTML payload into a <see cref="LyricsSyncKind.Word"/>
/// <see cref="LyricsDocument"/>: each <c>&lt;p&gt;</c> is a line and each timed child
/// <c>&lt;span begin/end&gt;</c> is a word. Single-lane only — translation, romanization and
/// background-vocal spans (<c>ttm:role="x-translation" | "x-roman" | "x-bg"</c>) are dropped, since
/// the renderer is single-lane in Phase 2. The same parser is the one the deferred AMLL-TTML-DB
/// provider will reuse, and the one a sideloaded override (<c>&lt;songId&gt;.ttml</c>) is parsed with.
/// </summary>
/// <remarks>
/// Element matching is namespace-agnostic (by local name) because AMLL files vary their declared
/// namespaces. The dialect is pinned to the AMLL TTML DB format spec; it is refined against a real
/// sample before being relied upon (see the Phase 2 design doc, §3.8 / §7).
/// </remarks>
public sealed class TtmlLyricsParser : ILyricsPayloadParser
{
    private static readonly char[] FormattingWhitespace = [' ', '\t', '\r', '\n'];

    private readonly ILogger _log;

    public TtmlLyricsParser(ILoggingService logService)
        => _log = logService.GetLogger(nameof(TtmlLyricsParser));

    public LyricsFormat Format => LyricsFormat.Ttml;

    public LyricsDocument? Parse(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return null;
        }

        XDocument xdoc;
        try
        {
            xdoc = XDocument.Parse(Encoding.UTF8.GetString(payload.Span), LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException ex)
        {
            _log.Warning("TTML payload is not well-formed XML; discarding", exception: ex);
            return null;
        }

        var body = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "body");
        if (body is null)
        {
            return null;
        }

        var lines = new List<LyricsLine>();
        foreach (var p in body.Descendants().Where(e => e.Name.LocalName == "p"))
        {
            if (TryBuildLine(p, out var line))
            {
                lines.Add(line);
            }
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var document = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            Format = LyricsFormat.Ttml,
            Lines = lines,
        };

        var issues = LyricsValidator.Validate(document);
        if (issues.Count > 0)
        {
            _log.Warning("TTML document failed validation; discarding",
                new { IssueCount = issues.Count, FirstIssue = issues[0] });
            return null;
        }

        return document;
    }

    /// <summary>
    /// Builds one line from a <c>&lt;p&gt;</c>: walks its child nodes in reading order, turning timed
    /// <c>&lt;span&gt;</c>s into words and folding literal text into the line. A line with text but no
    /// timed spans gets a single synthetic word covering it, so it still renders (and stays a valid
    /// Word document). Returns <c>false</c> for an empty line.
    /// </summary>
    private static bool TryBuildLine(XElement p, out LyricsLine line)
    {
        line = null!;

        var lineStart = Time(p, "begin");
        var lineEnd = Time(p, "end");

        var words = new List<LyricsWord>();
        var text = new StringBuilder();

        foreach (var node in p.Nodes())
        {
            switch (node)
            {
                case XText literal:
                    AppendLiteral(literal.Value, text, words);
                    break;

                case XElement span when span.Name.LocalName == "span":
                    if (IsAuxiliary(span))
                    {
                        break; // translation / romanization / background — not the single primary lane
                    }

                    var wordText = span.Value;
                    if (string.IsNullOrEmpty(wordText))
                    {
                        break;
                    }

                    var wordStart = Time(span, "begin") ?? lineStart ?? 0;
                    var wordEnd = Time(span, "end") ?? lineEnd ?? wordStart;
                    words.Add(new LyricsWord { StartMs = wordStart, EndMs = Math.Max(wordEnd, wordStart), Text = wordText });
                    text.Append(wordText);
                    break;
            }
        }

        var lineText = text.ToString().Trim();
        if (lineText.Length == 0 && words.Count == 0)
        {
            return false;
        }

        // A timed line with no word spans (plain <p>text</p>) → one synthetic word spanning the line, so
        // it renders as a line-level fill inside a Word document and keeps SyncKind=Word valid.
        if (words.Count == 0)
        {
            var start = lineStart ?? 0;
            var end = lineEnd ?? start;
            words.Add(new LyricsWord { StartMs = start, EndMs = Math.Max(end, start), Text = lineText });
        }

        // Normalize the (real-world) word timings: community AMLL files carry small word-start wobbles
        // (a syllable beginning a few tens of ms before the previous one) and zero-duration syllables.
        // Clamp starts monotonic and keep end ≥ start so one wobble doesn't fail validation and discard
        // the whole song; the few-ms shift is imperceptible in the wipe.
        words = NormalizeWordTimings(words);

        // Resolve / expand the line bounds so EVERY word is contained (the validator requires it): take
        // the min start / max end across all words, not just the first/last, and widen a present p@begin/
        // @end only when a word overruns it.
        var minStart = int.MaxValue;
        var maxEnd = int.MinValue;
        foreach (var word in words)
        {
            minStart = Math.Min(minStart, word.StartMs);
            maxEnd = Math.Max(maxEnd, word.EndMs);
        }

        var resolvedStart = Math.Min(lineStart ?? minStart, minStart);
        var resolvedEnd = Math.Max(lineEnd ?? maxEnd, maxEnd);

        line = new LyricsLine
        {
            StartMs = Math.Max(resolvedStart, 0),
            EndMs = resolvedEnd,
            Text = lineText,
            Words = words,
        };
        return true;
    }

    /// <summary>
    /// Clamps word starts to be non-decreasing and ends to be ≥ their start, absorbing the small timing
    /// wobbles and zero-duration syllables real AMLL files contain so a single bad word doesn't fail
    /// validation for the whole document. Word text is preserved.
    /// </summary>
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

    /// <summary>
    /// Appends a literal text node to the line. XML-formatting whitespace (any run containing a
    /// newline — i.e. editor indentation between elements) is dropped; genuine inter-word text is kept
    /// and also tacked onto the preceding word so its pixel extent (and the wipe) includes the gap.
    /// </summary>
    private static void AppendLiteral(string value, StringBuilder text, List<LyricsWord> words)
    {
        if (value.IndexOf('\n') >= 0)
        {
            value = value.Trim(FormattingWhitespace);
        }

        if (value.Length == 0)
        {
            return;
        }

        text.Append(value);
        if (words.Count > 0)
        {
            var last = words[^1];
            words[^1] = new LyricsWord { StartMs = last.StartMs, EndMs = last.EndMs, Text = last.Text + value };
        }
    }

    private static bool IsAuxiliary(XElement span)
    {
        var role = span.Attributes().FirstOrDefault(a => a.Name.LocalName == "role")?.Value;
        return role is "x-bg" or "x-translation" or "x-roman";
    }

    private static int? Time(XElement element, string attributeLocalName)
    {
        var value = element.Attributes().FirstOrDefault(a => a.Name.LocalName == attributeLocalName)?.Value;
        return ParseTime(value);
    }

    /// <summary>
    /// Parses a TTML time expression to milliseconds. Supports clock-time (<c>hh:mm:ss.fff</c>,
    /// <c>mm:ss.fff</c>) and offset-time seconds (<c>ss.fff</c> with an optional trailing <c>s</c>).
    /// Returns <c>null</c> for absent / unparseable values so the caller can fall back to line bounds.
    /// </summary>
    internal static int? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.EndsWith('s') && !value.Contains(':'))
        {
            value = value[..^1];
        }

        var parts = value.Split(':');
        try
        {
            double seconds = parts.Length switch
            {
                3 => int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600
                     + int.Parse(parts[1], CultureInfo.InvariantCulture) * 60
                     + double.Parse(parts[2], CultureInfo.InvariantCulture),
                2 => int.Parse(parts[0], CultureInfo.InvariantCulture) * 60
                     + double.Parse(parts[1], CultureInfo.InvariantCulture),
                1 => double.Parse(parts[0], CultureInfo.InvariantCulture),
                _ => double.NaN,
            };

            if (double.IsNaN(seconds) || seconds < 0)
            {
                return null;
            }

            return (int)Math.Round(seconds * 1000.0);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
