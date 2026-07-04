using System.Text;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Parses Netease's line-level LRC payload into a <see cref="LyricsSyncKind.Line"/> document via
/// <see cref="LrcLineParser"/>. Identical in shape to <c>LrcSyncedLyricsParser</c> but stamps the
/// <c>netease</c> source and the <c>netease-lrc</c> format tag, so a Netease line result is cached and
/// attributed distinctly from an lrclib one.
/// </summary>
public sealed class NeteaseLrcLyricsParser : ILyricsPayloadParser
{
    private readonly ILogger _log;

    public NeteaseLrcLyricsParser(ILoggingService logService)
        => _log = logService.GetLogger(nameof(NeteaseLrcLyricsParser));

    public string FormatTag => NeteaseLyricsConstants.LrcFormat;

    public LyricsDocument? Parse(ReadOnlyMemory<byte> payload)
    {
        var parsed = LrcLineParser.Parse(Encoding.UTF8.GetString(payload.Span));
        if (parsed.Lines.Count == 0)
        {
            return null;
        }

        var lines = new List<LyricsLine>(parsed.Lines.Count);
        foreach (var line in parsed.Lines)
        {
            lines.Add(new LyricsLine { StartMs = line.TimeMs, Text = line.Text });
        }

        var document = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Lines = lines,
            SourceProvider = NeteaseLyricsConstants.ProviderId,
            LanguageCode = null,
            OffsetMs = parsed.OffsetMs,
        };

        var issues = LyricsValidator.Validate(document);
        if (issues.Count > 0)
        {
            _log.Warning("Netease LRC document failed validation; discarding",
                new { IssueCount = issues.Count, FirstIssue = issues[0] });
            return null;
        }

        return document;
    }
}
