using System.Text.Json;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsWordTests
{
    [TestMethod]
    public void Construct_StoresAllFields()
    {
        var w = new LyricsWord { StartMs = 1000, EndMs = 1250, Text = "hello" };

        Assert.AreEqual(1000, w.StartMs);
        Assert.AreEqual(1250, w.EndMs);
        Assert.AreEqual("hello", w.Text);
    }

    [TestMethod]
    public void DurationMs_IsEndMinusStart()
    {
        var w = new LyricsWord { StartMs = 200, EndMs = 750, Text = "x" };
        Assert.AreEqual(550, w.DurationMs);
    }

    [TestMethod]
    public void DurationMs_ClampsToZeroWhenEndBeforeStart()
    {
        var w = new LyricsWord { StartMs = 500, EndMs = 200, Text = "x" };
        Assert.AreEqual(0, w.DurationMs);
    }

    [TestMethod]
    public void TextDefaultsToEmpty()
    {
        var w = new LyricsWord();
        Assert.AreEqual(string.Empty, w.Text);
    }

    [TestMethod]
    public void RoundTripsThroughJson()
    {
        var w = new LyricsWord { StartMs = 1000, EndMs = 1500, Text = "hi" };
        var json = JsonSerializer.Serialize(w, Json.Options);
        var back = JsonSerializer.Deserialize<LyricsWord>(json, Json.Options);

        Assert.IsNotNull(back);
        Assert.AreEqual(1000, back!.StartMs);
        Assert.AreEqual(1500, back.EndMs);
        Assert.AreEqual("hi", back.Text);
    }
}
