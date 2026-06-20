using System.Text;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Services.Lyrics;
using Hypersonic.Tests.Fakes;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LocalFileLyricsSourceTests
{
    private string _overrideFolder = null!;
    private LyricsParserRegistry _registry = null!;

    private const string WordTtml =
        """
        <tt xmlns="http://www.w3.org/ns/ttml">
          <body><div>
            <p begin="00:00.000" end="00:02.000">
              <span begin="00:00.000" end="00:01.000">over</span>
              <span begin="00:01.000" end="00:02.000"> ride</span>
            </p>
          </div></body>
        </tt>
        """;

    [TestInitialize]
    public void Setup()
    {
        _overrideFolder = Path.Combine(Path.GetTempPath(), "HypersonicOverrideTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_overrideFolder);
        var log = new LoggingService();
        _registry = new LyricsParserRegistry([new TtmlLyricsParser(log)], log);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_overrideFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private LocalFileLyricsSource NewSource()
        => new(new FakeFolderLocator(_overrideFolder), _registry, new NullLoggingService());

    private void WriteOverride(string songId, string content)
        => File.WriteAllBytes(Path.Combine(_overrideFolder, songId + ".ttml"), Encoding.UTF8.GetBytes(content));

    [TestMethod]
    public async Task NoFile_ReturnsNull()
    {
        var doc = await NewSource().TryGetOverrideAsync("song-1");
        Assert.IsNull(doc);
    }

    [TestMethod]
    public async Task PresentFile_ParsesToWordDocument()
    {
        WriteOverride("song-1", WordTtml);

        var doc = await NewSource().TryGetOverrideAsync("song-1");

        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Word, doc!.SyncKind);
        Assert.AreEqual("over ride", doc.Lines[0].Text);
        Assert.AreEqual(2, doc.Lines[0].Words.Count);
    }

    [TestMethod]
    public async Task UnparseableFile_ReturnsNull()
    {
        WriteOverride("song-1", "this is not ttml <<<");

        var doc = await NewSource().TryGetOverrideAsync("song-1");

        Assert.IsNull(doc);
    }

    [TestMethod]
    public async Task SongIdWithPathSeparator_IsRejected_NoTraversal()
    {
        // A would-be traversal id must never resolve to a file outside the override folder.
        var doc = await NewSource().TryGetOverrideAsync("../../evil");
        Assert.IsNull(doc);
    }

    [TestMethod]
    public async Task EmptyOrWhitespaceSongId_ReturnsNull()
    {
        Assert.IsNull(await NewSource().TryGetOverrideAsync(""));
        Assert.IsNull(await NewSource().TryGetOverrideAsync("   "));
    }

    private sealed class FakeFolderLocator : IAppFolderLocator
    {
        private readonly string _overrideFolder;
        public FakeFolderLocator(string overrideFolder) => _overrideFolder = overrideFolder;
        public string GetCacheFolder() => _overrideFolder;
        public string GetLyricsOverrideFolder() => _overrideFolder;
    }
}
