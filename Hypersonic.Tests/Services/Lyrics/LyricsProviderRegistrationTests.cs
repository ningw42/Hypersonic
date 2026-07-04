using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services.Lyrics;

using Microsoft.Extensions.DependencyInjection;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsProviderRegistrationTests
{
    private static ServiceProvider BuildLyricsSubset()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<ILyricsParserRegistry, LyricsParserRegistry>();

        // The CN word parsers (the subset added by this feature).
        services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
            LyricsFormat.Qrc, Lyricify.Lyrics.Models.LyricsRawTypes.Qrc,
            sp.GetRequiredService<ILoggingService>()));
        services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
            LyricsFormat.Krc, Lyricify.Lyrics.Models.LyricsRawTypes.Krc,
            sp.GetRequiredService<ILoggingService>()));
        services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
            LyricsFormat.Yrc, Lyricify.Lyrics.Models.LyricsRawTypes.Yrc,
            sp.GetRequiredService<ILoggingService>()));
        // The shared LRC parser (serves LyricsFormat.Lrc), so the registry can resolve line-tier payloads.
        services.AddSingleton<ILyricsPayloadParser, LrcSyncedLyricsParser>();

        services.AddSingleton<ILyricifyLyricsClient, LyricifyLyricsClient>();
        services.Configure<QQLyricsOptions>(_ => { });
        services.Configure<NeteaseLyricsOptions>(_ => { });
        services.Configure<KugouLyricsOptions>(_ => { });
        services.AddSingleton<ILyricsProvider, QQMusicLyricsProvider>();
        services.AddSingleton<ILyricsProvider, NeteaseLyricsProvider>();
        services.AddSingleton<ILyricsProvider, KugouLyricsProvider>();
        return services.BuildServiceProvider();
    }

    // NOTE: this exercises only the CN subset this feature adds, so it verifies the CN trio wires up
    // and resolves in qq → netease → kugou order. It does NOT prove the cross-provider position
    // (Subsonic → lrclib → AMLL → CN) — that lives in App.xaml.cs and is verified by inspection in Step 4.
    [TestMethod]
    public void LyricifyProvider_ResolveInQQNeteaseKugouOrder()
    {
        using var sp = BuildLyricsSubset();
        var ids = sp.GetServices<ILyricsProvider>().Select(p => p.Id).ToArray();
        CollectionAssert.AreEqual(new[] { "qq", "netease", "kugou" }, ids);
    }

    [TestMethod]
    public void Registry_ResolvesNewFormatTags()
    {
        using var sp = BuildLyricsSubset();
        var registry = sp.GetRequiredService<ILyricsParserRegistry>();
        // A format with no parser logs + returns null; a registered format must resolve. Feed a valid
        // LRC payload, which only produces a Line document if the shared LRC parser is registered.
        var doc = registry.Parse(LyricsFormat.Lrc,
            System.Text.Encoding.UTF8.GetBytes("[00:01.00]hi"));
        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Line, doc!.SyncKind);
    }
}
