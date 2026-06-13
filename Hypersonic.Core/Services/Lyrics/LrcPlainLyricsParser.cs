using System.Text;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Parses the cached <c>lrclib-plain</c> payload (plain-text lyrics) into a <see cref="LyricsSyncKind.Plain"/>
/// document: one line per text line, internal blank lines preserved as stanza breaks, trailing blank lines
/// trimmed. Source <c>lrclib</c>; validates and discards on failure (logging once).
/// </summary>
public sealed class LrcPlainLyricsParser : ILyricsPayloadParser
{
    private readonly ILogger _log;

    public LrcPlainLyricsParser(ILoggingService logService)
        => _log = logService.GetLogger(nameof(LrcPlainLyricsParser));

    public string FormatTag => LrcLibLyricsConstants.PlainFormat;

    public LyricsDocument? Parse(ReadOnlyMemory<byte> payload)
    {
        var text = Encoding.UTF8.GetString(payload.Span);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var end = raw.Length;
        while (end > 0 && string.IsNullOrWhiteSpace(raw[end - 1]))
        {
            end--;
        }

        if (end == 0)
        {
            return null;
        }

        var lines = new List<LyricsLine>(end);
        for (var i = 0; i < end; i++)
        {
            lines.Add(new LyricsLine { StartMs = 0, Text = raw[i] });
        }

        var document = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Plain,
            Lines = lines,
            SourceProvider = LrcLibLyricsConstants.ProviderId,
        };

        var issues = LyricsValidator.Validate(document);
        if (issues.Count > 0)
        {
            _log.Warning("lrclib plain document failed validation; discarding",
                new { IssueCount = issues.Count, FirstIssue = issues[0] });
            return null;
        }

        return document;
    }
}
