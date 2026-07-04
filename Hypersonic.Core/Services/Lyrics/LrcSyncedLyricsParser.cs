using System.Text;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Parses the cached <c>lrclib-synced</c> payload (raw LRC text) into a <see cref="LyricsSyncKind.Line"/>
/// document via <see cref="LrcLineParser"/>. Honors <c>[offset:]</c>, sets source <c>lrclib</c> and a null
/// language (lrclib carries none), validates, and discards a payload that yields no timed lines or fails
/// validation (logging once).
/// </summary>
public sealed class LrcSyncedLyricsParser : ILyricsPayloadParser
{
    private readonly ILogger _log;

    public LrcSyncedLyricsParser(ILoggingService logService)
        => _log = logService.GetLogger(nameof(LrcSyncedLyricsParser));

    public LyricsFormat Format => LyricsFormat.Lrc;

    public LyricsDocument? Parse(ReadOnlyMemory<byte> payload)
    {
        var parsed = LrcLineParser.Parse(Encoding.UTF8.GetString(payload.Span));
        if (parsed.Lines.Count == 0)
        {
            return null;
        }

        var lines = new List<LyricsLine>(parsed.Lines.Count);
        foreach (var l in parsed.Lines)
        {
            lines.Add(new LyricsLine { StartMs = l.TimeMs, Text = l.Text });
        }

        var document = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Format = LyricsFormat.Lrc,
            Lines = lines,
            LanguageCode = null,
            OffsetMs = parsed.OffsetMs,
        };

        var issues = LyricsValidator.Validate(document);
        if (issues.Count > 0)
        {
            _log.Warning("lrclib synced document failed validation; discarding",
                new { IssueCount = issues.Count, FirstIssue = issues[0] });
            return null;
        }

        return document;
    }
}
