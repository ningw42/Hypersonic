using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services.Lyrics;

/// <summary>The Kugou lyrics source (KRC, word-tier; the library decrypts the payload).</summary>
internal sealed class KugouLyricsProvider : LyricifyLyricsProviderBase
{
    private readonly KugouLyricsOptions _options;

    public KugouLyricsProvider(ILyricifyLyricsClient client, ILyricsParserRegistry parserRegistry,
        IOptions<KugouLyricsOptions> options, ILoggingService logService)
        : base(client, parserRegistry, logService)
        => _options = options.Value;

    public override string Id => KugouLyricsConstants.ProviderId;
    protected override LyricifyPlatform Platform => LyricifyPlatform.Kugou;
    protected override bool Enabled => _options.Enabled;
    protected override int MatchThreshold => _options.MatchThreshold;
}
