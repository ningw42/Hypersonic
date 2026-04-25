using Hypersonic.Core.Models;

namespace Hypersonic.Contracts.Services;

public interface IScrobblingService
{
    Task InitializeAsync();

    bool IsEnabled { get; }
    bool NowPlayingEnabled { get; }
    int PlaytimeThreshold { get; }
    int PercentageThreshold { get; }
    ScrobbleConditionMode ConditionMode { get; }

    Task SetEnabledAsync(bool enabled);
    Task SetNowPlayingEnabledAsync(bool enabled);
    Task SetPlaytimeThresholdAsync(int seconds);
    Task SetPercentageThresholdAsync(int percentage);
    Task SetConditionModeAsync(ScrobbleConditionMode mode);
}
