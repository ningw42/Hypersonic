using System.Text.Json;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsLineTests
{
    [TestMethod]
    public void Construct_StoresAllFields()
    {
        var line = new LyricsLine
        {
            StartMs = 5000,
            EndMs = 7500,
            Text = "Hello world",
            Words = new[]
            {
                new LyricsWord { StartMs = 5000, EndMs = 5500, Text = "Hello " },
                new LyricsWord { StartMs = 5500, EndMs = 7500, Text = "world" },
            },
        };

        Assert.AreEqual(5000, line.StartMs);
        Assert.AreEqual(7500, line.EndMs);
        Assert.AreEqual("Hello world", line.Text);
        Assert.AreEqual(2, line.Words.Count);
        Assert.AreEqual("Hello ", line.Words[0].Text);
    }

    [TestMethod]
    public void DefaultsAreSafe()
    {
        var line = new LyricsLine();

        Assert.AreEqual(0, line.StartMs);
        Assert.IsNull(line.EndMs);
        Assert.AreEqual(string.Empty, line.Text);
        Assert.AreEqual(0, line.Words.Count);
    }

    [TestMethod]
    public void Words_ExposedAsReadOnlyList()
    {
        var line = new LyricsLine
        {
            Words = new[] { new LyricsWord { StartMs = 0, EndMs = 100, Text = "x" } },
        };

        IReadOnlyList<LyricsWord> ro = line.Words;
        Assert.AreEqual(1, ro.Count);
    }

    [TestMethod]
    public void Words_SnapshotsTheInputEnumerable()
    {
        var mutable = new List<LyricsWord>
        {
            new() { StartMs = 0, EndMs = 100, Text = "a" },
        };
        var line = new LyricsLine { Words = mutable };

        mutable.Add(new LyricsWord { StartMs = 100, EndMs = 200, Text = "b" });

        Assert.AreEqual(1, line.Words.Count, "Mutating the source list must not affect the line.");
    }

    [TestMethod]
    public void RoundTripsThroughJson()
    {
        var line = new LyricsLine
        {
            StartMs = 1000,
            EndMs = 2000,
            Text = "hi",
            Words = new[] { new LyricsWord { StartMs = 1000, EndMs = 2000, Text = "hi" } },
        };

        var json = JsonSerializer.Serialize(line, Json.Options);
        var back = JsonSerializer.Deserialize<LyricsLine>(json, Json.Options);

        Assert.IsNotNull(back);
        Assert.AreEqual(1000, back!.StartMs);
        Assert.AreEqual(2000, back.EndMs);
        Assert.AreEqual("hi", back.Text);
        Assert.AreEqual(1, back.Words.Count);
        Assert.AreEqual("hi", back.Words[0].Text);
    }
}
