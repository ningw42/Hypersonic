using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// DI-populated <see cref="ILyricsParserRegistry"/>. Indexes the registered parsers by their
/// <see cref="ILyricsPayloadParser.FormatTag"/> so a stored payload's format tag selects its
/// parser independently of which provider originally fetched it.
/// </summary>
public sealed class LyricsParserRegistry : ILyricsParserRegistry
{
    private readonly Dictionary<string, ILyricsPayloadParser> _parsers;
    private readonly ILogger _log;

    public LyricsParserRegistry(IEnumerable<ILyricsPayloadParser> parsers, ILoggingService logService)
    {
        _parsers = new Dictionary<string, ILyricsPayloadParser>(StringComparer.Ordinal);
        foreach (var parser in parsers)
        {
            _parsers[parser.FormatTag] = parser;
        }

        _log = logService.GetLogger(nameof(LyricsParserRegistry));
    }

    public LyricsDocument? Parse(string formatTag, ReadOnlyMemory<byte> payload)
    {
        if (string.IsNullOrEmpty(formatTag) || !_parsers.TryGetValue(formatTag, out var parser))
        {
            _log.Warning("No lyrics parser registered for format tag", new { FormatTag = formatTag });
            return null;
        }

        return parser.Parse(payload);
    }
}
