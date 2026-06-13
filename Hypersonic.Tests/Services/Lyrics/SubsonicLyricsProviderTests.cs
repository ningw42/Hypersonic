using System.Text;

using Hypersonic.Client.Models.Entities;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Services.Lyrics;
using Hypersonic.Tests.Fakes;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class SubsonicLyricsProviderTests
{
    private static SubsonicLyricsProvider Provider(FakeLyricsSubsonicClient client)
    {
        var log = new LoggingService();
        var registry = new LyricsParserRegistry(
            [new SubsonicStructuredLyricsParser(log), new SubsonicLegacyLyricsParser(log)],
            log);
        var clientService = new FakeSubsonicClientService { Client = client };
        return new SubsonicLyricsProvider(clientService, registry, log);
    }

    private static SubsonicLyricsProvider ProviderWithoutClient()
    {
        var log = new LoggingService();
        var registry = new LyricsParserRegistry(
            [new SubsonicStructuredLyricsParser(log), new SubsonicLegacyLyricsParser(log)],
            log);
        return new SubsonicLyricsProvider(new FakeSubsonicClientService { Client = null }, registry, log);
    }

    private static LyricsQuery Query => new("song-1", Artist: "Artist", Title: "Title");

    private static GetLyricsBySongIdResponse StructuredOk(params StructuredLyrics[] entries) => new()
    {
        Status = "ok",
        LyricsList = new LyricsList { StructuredLyrics = entries },
    };

    private static GetLyricsBySongIdResponse StructuredFailed(int code) => new()
    {
        Status = "failed",
        Error = new SubsonicError { Code = code, Message = "x" },
    };

    private static GetLyricsResponse LegacyOk(string text) => new()
    {
        Status = "ok",
        Lyrics = new Client.Models.Entities.Lyrics { Value = text },
    };

    private static GetLyricsResponse LegacyFailed(int code) => new()
    {
        Status = "failed",
        Error = new SubsonicError { Code = code, Message = "x" },
    };

    private static StructuredLyrics SyncedEntry() => new()
    {
        Synced = true,
        Line = [new LyricLine { Start = 0, Value = "structured line" }],
    };

    [TestMethod]
    public async Task NoActiveClient_ReturnsTransient()
    {
        var result = await ProviderWithoutClient().FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, result.Kind);
    }

    [TestMethod]
    public async Task StructuredContent_ShortCircuits_DoesNotCallLegacy()
    {
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => Task.FromResult(StructuredOk(SyncedEntry())),
            LegacyHandler = (_, _) => throw new InvalidOperationException("legacy must not be called"),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.IsTrue(result.Definitive);
        Assert.AreEqual(SubsonicLyricsConstants.StructuredFormat, result.PayloadFormat);
        Assert.AreEqual(LyricsSyncKind.Line, result.Document!.SyncKind);
        Assert.AreEqual(0, client.LegacyCallCount);
    }

    [TestMethod]
    public async Task Structured_PrefersSyncedEntry_OverEarlierPlainEntry()
    {
        // A server may return multiple structuredLyrics entries. A plain entry appearing before a
        // synced one must not win — line-synced lyrics are the better answer.
        var plain = new StructuredLyrics { Synced = false, Line = [new LyricLine { Value = "plain line" }] };
        var synced = new StructuredLyrics { Synced = true, Line = [new LyricLine { Start = 0, Value = "synced line" }] };
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => Task.FromResult(StructuredOk(plain, synced)), // plain listed first
            LegacyHandler = (_, _) => throw new InvalidOperationException("legacy must not be called"),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.AreEqual(LyricsSyncKind.Line, result.Document!.SyncKind);
        Assert.AreEqual("synced line", result.Document.Lines[0].Text);
    }

    [TestMethod]
    public async Task Structured_PlainOnlyEntry_ReturnsPlainWithoutCallingLegacy()
    {
        var plain = new StructuredLyrics { Synced = false, Line = [new LyricLine { Value = "plain only" }] };
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => Task.FromResult(StructuredOk(plain)),
            LegacyHandler = (_, _) => throw new InvalidOperationException("legacy must not be called"),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.AreEqual(LyricsSyncKind.Plain, result.Document!.SyncKind);
        Assert.AreEqual(0, client.LegacyCallCount);
    }

    [TestMethod]
    public async Task StructuredEmpty_LegacyContent_ContentIsDefinitive()
    {
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => Task.FromResult(StructuredFailed(70)),
            LegacyHandler = (_, _) => Task.FromResult(LegacyOk("legacy text")),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.IsTrue(result.Definitive);
        Assert.AreEqual(SubsonicLyricsConstants.LegacyFormat, result.PayloadFormat);
        Assert.AreEqual("legacy text", Encoding.UTF8.GetString(result.RawPayload.Span));
        Assert.AreEqual(LyricsSyncKind.Plain, result.Document!.SyncKind);
    }

    [TestMethod]
    public async Task StructuredUnsupported_LegacyContent_ContentIsDefinitive()
    {
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => Task.FromResult(StructuredFailed(30)),
            LegacyHandler = (_, _) => Task.FromResult(LegacyOk("legacy text")),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.IsTrue(result.Definitive);
    }

    [TestMethod]
    public async Task StructuredTransient_LegacyContent_ContentIsNotDefinitive()
    {
        // The single definitive:false case — a transient on the higher-quality endpoint
        // plus content on the fallback: show it, do not cache it.
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => throw new HttpRequestException("structured transient"),
            LegacyHandler = (_, _) => Task.FromResult(LegacyOk("legacy text")),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.IsFalse(result.Definitive);
    }

    [TestMethod]
    public async Task StructuredTransientFailedCode_LegacyContent_ContentIsNotDefinitive()
    {
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => Task.FromResult(StructuredFailed(0)),
            LegacyHandler = (_, _) => Task.FromResult(LegacyOk("legacy text")),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.IsFalse(result.Definitive);
    }

    [TestMethod]
    public async Task BothEmpty_ReturnsDefinitiveOutcome()
    {
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => Task.FromResult(StructuredFailed(70)),
            LegacyHandler = (_, _) => Task.FromResult(LegacyFailed(70)),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreNotEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.IsTrue(result.IsDefinitive);
    }

    [TestMethod]
    public async Task StructuredEmpty_LegacyTransient_ReturnsTransient()
    {
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => Task.FromResult(StructuredFailed(70)),
            LegacyHandler = (_, _) => throw new HttpRequestException("legacy transient"),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreNotEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.IsFalse(result.IsDefinitive);
    }

    [TestMethod]
    public async Task BothTransient_ReturnsTransient()
    {
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => throw new HttpRequestException("s"),
            LegacyHandler = (_, _) => throw new HttpRequestException("l"),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.IsFalse(result.IsDefinitive);
    }

    [TestMethod]
    public async Task StructuredBlankContent_FallsThroughToLegacy()
    {
        // Structured 'ok' but with no usable entry behaves like definitive-empty: legacy runs.
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => Task.FromResult(StructuredOk(new StructuredLyrics
            {
                Synced = false,
                Line = [new LyricLine { Value = "   " }],
            })),
            LegacyHandler = (_, _) => Task.FromResult(LegacyOk("legacy text")),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.AreEqual(SubsonicLyricsConstants.LegacyFormat, result.PayloadFormat);
        Assert.IsTrue(result.Definitive);
        Assert.AreEqual(1, client.LegacyCallCount);
    }

    [TestMethod]
    public async Task ProviderId_IsSubsonic()
    {
        Assert.AreEqual("subsonic", ProviderWithoutClient().Id);
    }

    [TestMethod]
    public async Task FetchAsync_AlreadyCancelled_ThrowsBeforeStructured()
    {
        // The provider contract must observe cancellation before doing any work — a pre-cancelled
        // token may not hit the structured endpoint or report success.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => throw new InvalidOperationException("structured must not be called"),
            LegacyHandler = (_, _) => throw new InvalidOperationException("legacy must not be called"),
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await Provider(client).FetchAsync(Query, cts.Token));
        Assert.AreEqual(0, client.StructuredCallCount, "structured must not run when the token is already cancelled");
    }

    [TestMethod]
    public async Task FetchAsync_CancelledBetweenEndpoints_ThrowsBeforeLegacy()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeLyricsSubsonicClient
        {
            // Structured returns definitive-empty (would normally fall to legacy), but cancels first.
            StructuredHandler = _ => { cts.Cancel(); return Task.FromResult(StructuredFailed(70)); },
            LegacyHandler = (_, _) => Task.FromResult(LegacyOk("legacy")),
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await Provider(client).FetchAsync(Query, cts.Token));
        Assert.AreEqual(0, client.LegacyCallCount, "legacy must not run after cancellation");
    }

    [TestMethod]
    public async Task FetchAsync_CallerCancellationFromClient_Propagates_NotSwallowedAsTransient()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); },
            LegacyHandler = (_, _) => Task.FromResult(LegacyOk("legacy")),
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await Provider(client).FetchAsync(Query, cts.Token));
        Assert.AreEqual(0, client.LegacyCallCount);
    }

    [TestMethod]
    public async Task FetchAsync_StructuredTimeout_TreatedAsTransient_FallsToLegacy()
    {
        // A timeout surfaces as TaskCanceledException while OUR token is not cancelled — that is a
        // transient failure, not caller cancellation, so the legacy fallback still runs.
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => throw new TaskCanceledException("timeout"),
            LegacyHandler = (_, _) => Task.FromResult(LegacyOk("legacy")),
        };

        var result = await Provider(client).FetchAsync(Query, CancellationToken.None);

        Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.AreEqual("legacy", Encoding.UTF8.GetString(result.RawPayload.Span));
    }

    [TestMethod]
    public async Task FetchAsync_ThreadsCancellationToken_ToStructuredAndLegacyCalls()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeLyricsSubsonicClient
        {
            StructuredHandler = _ => Task.FromResult(StructuredFailed(70)), // empty → falls to legacy
            LegacyHandler = (_, _) => Task.FromResult(LegacyOk("legacy text")),
        };

        await Provider(client).FetchAsync(Query, cts.Token);

        Assert.AreEqual(cts.Token, client.LastStructuredToken, "structured call must receive the provider token");
        Assert.AreEqual(cts.Token, client.LastLegacyToken, "legacy call must receive the provider token");
    }
}
