using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services.Lyrics;

/// <summary>The Netease Cloud Music lyrics source (YRC word when present, else LRC line).</summary>
internal sealed class NeteaseLyricsProvider : CnLyricsProviderBase
{
    private readonly NeteaseLyricsOptions _options;

    public NeteaseLyricsProvider(ILyricifyLyricsClient client, ILyricsParserRegistry parserRegistry,
        IOptions<NeteaseLyricsOptions> options, ILoggingService logService)
        : base(client, parserRegistry, logService)
        => _options = options.Value;

    public override string Id => NeteaseLyricsConstants.ProviderId;
    protected override LyricifyPlatform Platform => LyricifyPlatform.Netease;
    protected override bool Enabled => _options.Enabled;
    protected override int MatchThreshold => _options.MatchThreshold;
}
