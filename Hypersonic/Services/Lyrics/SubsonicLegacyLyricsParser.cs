using System.Text;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Parses the cached <c>subsonic-legacy</c> payload (the plain-text lyrics blob) into a
/// <see cref="LyricsSyncKind.Plain"/> <see cref="LyricsDocument"/>: one line per text line,
/// internal blank lines preserved as stanza breaks, trailing blank lines trimmed.
/// </summary>
internal sealed class SubsonicLegacyLyricsParser : ILyricsPayloadParser
{
    private readonly ILogger _log;

    public SubsonicLegacyLyricsParser(ILoggingService logService)
    {
        _log = logService.GetLogger(nameof(SubsonicLegacyLyricsParser));
    }

    public string FormatTag => SubsonicLyricsConstants.LegacyFormat;

    public LyricsDocument? Parse(ReadOnlyMemory<byte> payload)
    {
        var text = Encoding.UTF8.GetString(payload.Span);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // Trim trailing blank lines (a final newline must not add a phantom line); keep internal blanks.
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
            SourceProvider = SubsonicLyricsConstants.ProviderId,
        };

        var issues = LyricsValidator.Validate(document);
        if (issues.Count > 0)
        {
            _log.Warning(
                "Legacy lyrics document failed validation; discarding",
                new { IssueCount = issues.Count, FirstIssue = issues[0] });
            return null;
        }

        return document;
    }
}
