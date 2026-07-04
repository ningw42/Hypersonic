using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// DI-populated <see cref="ILyricsParserRegistry"/>. Indexes the registered parsers by their
/// <see cref="ILyricsPayloadParser.Format"/> so a stored payload's format selects its
/// parser independently of which provider originally fetched it.
/// </summary>
public sealed class LyricsParserRegistry : ILyricsParserRegistry
{
    private readonly Dictionary<LyricsFormat, ILyricsPayloadParser> _parsers;
    private readonly ILogger _log;

    public LyricsParserRegistry(IEnumerable<ILyricsPayloadParser> parsers, ILoggingService logService)
    {
        _parsers = new Dictionary<LyricsFormat, ILyricsPayloadParser>();
        foreach (var parser in parsers)
        {
            _parsers[parser.Format] = parser;
        }

        _log = logService.GetLogger(nameof(LyricsParserRegistry));
    }

    public LyricsDocument? Parse(LyricsFormat format, ReadOnlyMemory<byte> payload)
    {
        if (!_parsers.TryGetValue(format, out var parser))
        {
            _log.Warning("No lyrics parser registered for format", new { Format = format });
            return null;
        }

        return parser.Parse(payload);
    }
}
