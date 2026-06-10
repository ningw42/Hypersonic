using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class LyricsParserRegistryTests
{
    private sealed class FakeParser : ILyricsPayloadParser
    {
        private readonly Func<ReadOnlyMemory<byte>, LyricsDocument?> _parse;

        public FakeParser(string formatTag, Func<ReadOnlyMemory<byte>, LyricsDocument?> parse)
        {
            FormatTag = formatTag;
            _parse = parse;
        }

        public string FormatTag { get; }

        public LyricsDocument? Parse(ReadOnlyMemory<byte> payload) => _parse(payload);
    }

    private static LyricsParserRegistry Create(params ILyricsPayloadParser[] parsers)
        => new(parsers, new LoggingService());

    [TestMethod]
    public void Parse_DispatchesToParserMatchingFormatTag()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Plain,
            Lines = new[] { new LyricsLine { Text = "hi" } },
        };
        var registry = Create(
            new FakeParser("a", _ => throw new InvalidOperationException("wrong parser selected")),
            new FakeParser("b", _ => doc));

        var result = registry.Parse("b", new byte[] { 1, 2, 3 });

        Assert.AreSame(doc, result);
    }

    [TestMethod]
    public void Parse_UnknownFormatTag_ReturnsNull()
    {
        var registry = Create(new FakeParser("a", _ => new LyricsDocument()));

        Assert.IsNull(registry.Parse("missing", new byte[] { 1 }));
    }

    [TestMethod]
    public void Parse_EmptyFormatTag_ReturnsNull()
    {
        var registry = Create(new FakeParser("a", _ => new LyricsDocument()));

        Assert.IsNull(registry.Parse(string.Empty, new byte[] { 1 }));
    }

    [TestMethod]
    public void Parse_PassesPayloadBytesThroughToParser()
    {
        byte[]? captured = null;
        var registry = Create(new FakeParser("a", bytes =>
        {
            captured = bytes.ToArray();
            return null;
        }));

        registry.Parse("a", new byte[] { 9, 8, 7 });

        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, captured);
    }
}
