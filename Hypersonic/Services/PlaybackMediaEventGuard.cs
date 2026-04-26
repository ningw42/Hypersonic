namespace Hypersonic.Services;

internal enum MediaEndedGuardResult
{
    Accept,
    IgnorePendingOpen,
    IgnoreBeforeDuration,
}

internal static class PlaybackMediaEventGuard
{
    public static bool IsEventForCurrentSource(
        int eventSourceGeneration,
        int currentSourceGeneration,
        bool hasCurrentPlaybackItem)
    {
        return hasCurrentPlaybackItem && eventSourceGeneration == currentSourceGeneration;
    }

    public static MediaEndedGuardResult EvaluateMediaEnded(
        bool isPendingPlayOnOpen,
        double positionSeconds,
        double durationSeconds)
    {
        if (isPendingPlayOnOpen)
        {
            return MediaEndedGuardResult.IgnorePendingOpen;
        }

        if (durationSeconds > 1 && positionSeconds < durationSeconds - 1)
        {
            return MediaEndedGuardResult.IgnoreBeforeDuration;
        }

        return MediaEndedGuardResult.Accept;
    }
}
