using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Services;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsSourceCatalogTests
{
    private sealed class FakeProvider(string id, string displayName) : ILyricsProvider
    {
        public string Id => id;
        public string DisplayName => displayName;
        public Task<LyricsProviderResult> FetchAsync(LyricsQuery q, CancellationToken ct)
            => Task.FromResult(LyricsProviderResult.Transient);
    }

    private static ILyricsSourceCatalog Catalog()
        => new LyricsSourceCatalog(new ILyricsProvider[]
        {
            new FakeProvider("lrclib", "LrcLib"),
            new FakeProvider("amll-ttml-db", "AMLL"),
        });

    [TestMethod]
    public void DisplayNameFor_KnownId_ReturnsName()
        => Assert.AreEqual("LrcLib", Catalog().DisplayNameFor("lrclib"));

    [TestMethod]
    public void DisplayNameFor_UnknownId_ReturnsNull()
        => Assert.IsNull(Catalog().DisplayNameFor("ttml"));

    [TestMethod]
    public void DisplayNameFor_Null_ReturnsNull()
        => Assert.IsNull(Catalog().DisplayNameFor(null));
}
