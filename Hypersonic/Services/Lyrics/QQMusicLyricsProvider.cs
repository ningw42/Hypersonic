using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services.Lyrics;

/// <summary>The QQ Music lyrics source (QRC, word-tier).</summary>
internal sealed class QQMusicLyricsProvider : LyricifyLyricsProviderBase
{
    private readonly QQLyricsOptions _options;

    public QQMusicLyricsProvider(ILyricifyLyricsClient client, ILyricsParserRegistry parserRegistry,
        IOptions<QQLyricsOptions> options, ILoggingService logService)
        : base(client, parserRegistry, logService)
        => _options = options.Value;

    public override string Id => QQLyricsConstants.ProviderId;
    public override string DisplayName => "QQ Music";
    protected override LyricifyPlatform Platform => LyricifyPlatform.QQMusic;
    protected override bool Enabled => _options.Enabled;
    protected override int MatchThreshold => _options.MatchThreshold;
}
