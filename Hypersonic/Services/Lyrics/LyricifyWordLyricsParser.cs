using System.Text;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;

using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// A word-tier <see cref="ILyricsPayloadParser"/> for a library-parseable syllable format (QRC / KRC /
/// YRC). Parametrized by format tag, the stamped source id, and the library raw type so one class
/// serves all three (registered three times in DI). Parses the raw text via the library, maps through
/// <see cref="LyricifySyllableMapper"/>, validates, and discards an invalid document.
/// </summary>
internal sealed class LyricifyWordLyricsParser : ILyricsPayloadParser
{
    private readonly string _formatTag;
    private readonly string _sourceId;
    private readonly LyricsRawTypes _rawType;
    private readonly ILogger _log;

    public LyricifyWordLyricsParser(string formatTag, string sourceId, LyricsRawTypes rawType, ILoggingService logService)
    {
        _formatTag = formatTag;
        _sourceId = sourceId;
        _rawType = rawType;
        _log = logService.GetLogger(nameof(LyricifyWordLyricsParser));
    }

    public string FormatTag => _formatTag;

    public LyricsDocument? Parse(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return null;
        }

        List<ILineInfo>? lines;
        try
        {
            lines = ParseHelper.ParseLyrics(Encoding.UTF8.GetString(payload.Span), _rawType)?.Lines;
        }
        catch (Exception ex)
        {
            _log.Warning("Lyricify parse threw; discarding", new { _formatTag }, ex);
            return null;
        }

        if (lines is null || lines.Count == 0)
        {
            return null;
        }

        var document = LyricifySyllableMapper.ToWordDocument(lines, _sourceId);
        if (document is null)
        {
            return null;
        }

        var issues = LyricsValidator.Validate(document);
        if (issues.Count > 0)
        {
            _log.Warning($"{_formatTag} document failed validation; discarding",
                new { IssueCount = issues.Count, FirstIssue = issues[0] });
            return null;
        }

        return document;
    }
}
