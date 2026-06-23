using Hypersonic.Core.Contracts.Services;

namespace Hypersonic.Core.Tests.Contracts;

[TestClass]
public class PlaybackInterfaceTests
{
    [TestMethod]
    public void IPlaybackService_Extends_IReadOnlyPlaybackState()
    {
        Assert.IsTrue(
            typeof(IReadOnlyPlaybackState).IsAssignableFrom(typeof(IPlaybackService)),
            "IPlaybackService must extend the read-only observation surface");
    }
}
