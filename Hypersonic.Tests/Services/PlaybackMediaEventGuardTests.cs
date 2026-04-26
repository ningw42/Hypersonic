using Hypersonic.Services;

namespace Hypersonic.Tests.Services;

[TestClass]
public class PlaybackMediaEventGuardTests
{
    [TestMethod]
    public void IsEventForCurrentSource_RejectsPreviousSourceGeneration()
    {
        var isCurrent = PlaybackMediaEventGuard.IsEventForCurrentSource(
            eventSourceGeneration: 1,
            currentSourceGeneration: 2,
            hasCurrentPlaybackItem: true);

        Assert.IsFalse(isCurrent);
    }

    [TestMethod]
    public void IsEventForCurrentSource_RejectsWhenNoPlaybackItemIsCurrent()
    {
        var isCurrent = PlaybackMediaEventGuard.IsEventForCurrentSource(
            eventSourceGeneration: 2,
            currentSourceGeneration: 2,
            hasCurrentPlaybackItem: false);

        Assert.IsFalse(isCurrent);
    }

    [TestMethod]
    public void IsEventForCurrentSource_AcceptsMatchingSourceGeneration()
    {
        var isCurrent = PlaybackMediaEventGuard.IsEventForCurrentSource(
            eventSourceGeneration: 2,
            currentSourceGeneration: 2,
            hasCurrentPlaybackItem: true);

        Assert.IsTrue(isCurrent);
    }

    [TestMethod]
    public void EvaluateMediaEnded_IgnoresEventWhileCurrentTrackIsStillOpening()
    {
        var result = PlaybackMediaEventGuard.EvaluateMediaEnded(
            isPendingPlayOnOpen: true,
            positionSeconds: 0,
            durationSeconds: 30);

        Assert.AreEqual(MediaEndedGuardResult.IgnorePendingOpen, result);
    }

    [TestMethod]
    public void EvaluateMediaEnded_IgnoresEventBeforeCurrentTrackDuration()
    {
        var result = PlaybackMediaEventGuard.EvaluateMediaEnded(
            isPendingPlayOnOpen: false,
            positionSeconds: 4,
            durationSeconds: 30);

        Assert.AreEqual(MediaEndedGuardResult.IgnoreBeforeDuration, result);
    }

    [TestMethod]
    public void EvaluateMediaEnded_AcceptsEventNearCurrentTrackDuration()
    {
        var result = PlaybackMediaEventGuard.EvaluateMediaEnded(
            isPendingPlayOnOpen: false,
            positionSeconds: 29.5,
            durationSeconds: 30);

        Assert.AreEqual(MediaEndedGuardResult.Accept, result);
    }

    [TestMethod]
    public void EvaluateMediaEnded_AcceptsUnknownDuration()
    {
        var result = PlaybackMediaEventGuard.EvaluateMediaEnded(
            isPendingPlayOnOpen: false,
            positionSeconds: 0,
            durationSeconds: 0);

        Assert.AreEqual(MediaEndedGuardResult.Accept, result);
    }
}
