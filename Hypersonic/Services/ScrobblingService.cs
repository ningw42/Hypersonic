using System.Text.Json;

using Hypersonic.Client;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Services;

public class ScrobblingService : IScrobblingService
{
    private const string EnabledKey = "ScrobblingEnabled";
    private const string NowPlayingKey = "ScrobbleNowPlaying";
    private const string PlaytimeKey = "ScrobblePlaytimeThreshold";
    private const string PercentageKey = "ScrobblePercentageThreshold";
    private const string ConditionModeKey = "ScrobbleConditionMode";

    private static readonly HashSet<int> ValidPlaytimePresets = [15, 30, 60, 120, 240];

    private readonly IPlaybackService _playbackService;
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    private readonly object _initLock = new();
    private Task? _initializeTask;

    private string? _trackedTrackId;
    private string? _trackedTitle;
    private double _trackedDuration;
    private double _accumulatedPlaytime;
    private long _segmentStart;
    private bool _isSegmentActive;
    private bool _hasScrobbled;
    private bool _nowPlayingSent;
    private string? _pendingNowPlayingTrackId;
    private Task _nowPlayingSendTask = Task.CompletedTask;
    private Task _lastScrobbleTask = Task.CompletedTask;

    private CancellationTokenSource? _percentageDebounceCts;

    public bool IsEnabled { get; private set; }
    public bool NowPlayingEnabled { get; private set; } = true;
    public int PlaytimeThreshold { get; private set; } = 30;
    public int PercentageThreshold { get; private set; } = 50;
    public ScrobbleConditionMode ConditionMode { get; private set; }

    internal Task PendingScrobbleTask => _lastScrobbleTask;
    internal Task PendingNowPlayingTask => _nowPlayingSendTask;

    public ScrobblingService(
        IPlaybackService playbackService,
        ISubsonicClientService subsonicClientService,
        ILocalSettingsService localSettingsService,
        ILoggingService logService,
        TimeProvider timeProvider)
    {
        _playbackService = playbackService;
        _subsonicClientService = subsonicClientService;
        _localSettingsService = localSettingsService;
        _logger = logService.GetLogger(nameof(ScrobblingService));
        _timeProvider = timeProvider;
    }

    public Task InitializeAsync()
    {
        lock (_initLock)
        {
            if (_initializeTask is { IsFaulted: true })
                _initializeTask = null;
            return _initializeTask ??= InitializeCoreAsync();
        }
    }

    private async Task InitializeCoreAsync()
    {
        IsEnabled = await ReadValidatedSettingAsync(EnabledKey, false);
        NowPlayingEnabled = await ReadValidatedSettingAsync(NowPlayingKey, true);
        PlaytimeThreshold = await ReadValidatedSettingAsync(PlaytimeKey, 30,
            v => ValidPlaytimePresets.Contains(v));
        PercentageThreshold = await ReadValidatedSettingAsync(PercentageKey, 50,
            v => v >= 10 && v <= 100 && v % 5 == 0);
        ConditionMode = await ReadValidatedSettingAsync(ConditionModeKey, ScrobbleConditionMode.Or,
            v => Enum.IsDefined(v));

        _playbackService.TrackChanged += OnTrackChanged;
        _playbackService.TrackEnded += OnTrackEnded;
        _playbackService.PositionChanged += OnPositionChanged;
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;

        if (_playbackService.NowPlaying != null)
        {
            _trackedTrackId = _playbackService.NowPlaying.Id;
            _trackedTitle = _playbackService.NowPlaying.Title;
            _trackedDuration = _playbackService.Duration;
            if (IsEnabled && _playbackService.IsPlaying)
            {
                _segmentStart = _timeProvider.GetTimestamp();
                _isSegmentActive = true;
                TrySendNowPlaying();
            }
        }
    }

    private async Task<T> ReadValidatedSettingAsync<T>(string key, T defaultValue, Func<T, bool>? validate = null)
    {
        try
        {
            var value = await _localSettingsService.ReadSettingAsync<T>(key);
            if (value is null) return defaultValue;
            if (validate != null && !validate(value))
            {
                _logger.Debug($"Setting '{key}' failed validation, using default",
                    context: new { key, value, defaultValue });
                return defaultValue;
            }
            return value;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or InvalidCastException)
        {
            _logger.Warning($"Setting '{key}' unreadable, using default",
                context: new { key, defaultValue }, exception: ex);
            return defaultValue;
        }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (enabled)
        {
            IsEnabled = true;
            _trackedTrackId = _playbackService.NowPlaying?.Id;
            _trackedTitle = _playbackService.NowPlaying?.Title;
            _trackedDuration = _playbackService.Duration;
            _accumulatedPlaytime = 0;
            _hasScrobbled = false;
            _nowPlayingSent = false;
            _isSegmentActive = false;

            if (_playbackService.IsPlaying && _trackedTrackId != null)
            {
                _segmentStart = _timeProvider.GetTimestamp();
                _isSegmentActive = true;
                TrySendNowPlaying();
            }
        }
        else
        {
            IsEnabled = false;
            _trackedTrackId = null;
            _trackedTitle = null;
            _trackedDuration = 0;
            _accumulatedPlaytime = 0;
            _hasScrobbled = false;
            _nowPlayingSent = false;
            _isSegmentActive = false;
        }

        await SaveSettingSafeAsync(EnabledKey, enabled);
    }

    public async Task SetNowPlayingEnabledAsync(bool enabled)
    {
        NowPlayingEnabled = enabled;
        await SaveSettingSafeAsync(NowPlayingKey, enabled);
    }

    public async Task SetPlaytimeThresholdAsync(int seconds)
    {
        if (!ValidPlaytimePresets.Contains(seconds)) return;
        PlaytimeThreshold = seconds;
        await SaveSettingSafeAsync(PlaytimeKey, seconds);
    }

    public async Task SetPercentageThresholdAsync(int percentage)
    {
        if (percentage < 10 || percentage > 100 || percentage % 5 != 0) return;
        PercentageThreshold = percentage;

        _percentageDebounceCts?.Cancel();
        _percentageDebounceCts = new CancellationTokenSource();
        var token = _percentageDebounceCts.Token;

        try
        {
            await Task.Delay(500, token);
            await SaveSettingSafeAsync(PercentageKey, percentage);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task SetConditionModeAsync(ScrobbleConditionMode mode)
    {
        if (!Enum.IsDefined(mode)) return;
        ConditionMode = mode;
        await SaveSettingSafeAsync(ConditionModeKey, mode);
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        _trackedTrackId = _playbackService.NowPlaying?.Id;
        _trackedTitle = _playbackService.NowPlaying?.Title;
        _trackedDuration = _playbackService.Duration;
        _accumulatedPlaytime = 0;
        _hasScrobbled = false;
        _nowPlayingSent = false;
        _isSegmentActive = false;
    }

    private void OnTrackEnded(object? sender, TrackEndedEventArgs e)
    {
        if (!IsEnabled || _trackedTrackId == null || e.TrackId != _trackedTrackId) return;

        if (_isSegmentActive)
        {
            _accumulatedPlaytime += _timeProvider.GetElapsedTime(_segmentStart).TotalSeconds;
            _isSegmentActive = false;
        }

        if (_trackedDuration <= 0)
            _trackedDuration = e.Duration;

        if (!_hasScrobbled && _accumulatedPlaytime > 0)
        {
            double effectivePlaytime;
            if (_trackedDuration > 0 && _trackedDuration < PlaytimeThreshold)
                effectivePlaytime = Math.Max(_trackedDuration - 1.0, 0);
            else
                effectivePlaytime = PlaytimeThreshold;

            var playtimeMet = _accumulatedPlaytime >= effectivePlaytime;

            double actualPercentage;
            if (_trackedDuration > 0)
            {
                if (_accumulatedPlaytime >= _trackedDuration - 2.0)
                    actualPercentage = 100.0;
                else
                    actualPercentage = _accumulatedPlaytime / _trackedDuration * 100;
            }
            else
            {
                actualPercentage = 0;
            }

            var percentageMet = actualPercentage >= PercentageThreshold;

            var shouldScrobble = ConditionMode == ScrobbleConditionMode.Or
                ? playtimeMet || percentageMet
                : playtimeMet && percentageMet;

            if (shouldScrobble)
            {
                _hasScrobbled = true;
                _lastScrobbleTask = FireScrobbleAsync(_trackedTrackId, _trackedTitle);
            }
        }
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (!IsEnabled || _hasScrobbled || _trackedTrackId == null) return;

        if (_playbackService.IsPlaying && !_isSegmentActive && IsEnabled)
        {
            _segmentStart = _timeProvider.GetTimestamp();
            _isSegmentActive = true;
            TrySendNowPlaying();
            return;
        }

        if (!_isSegmentActive) return;

        var currentPlaytime = _accumulatedPlaytime
            + _timeProvider.GetElapsedTime(_segmentStart).TotalSeconds;

        if (_trackedDuration <= 0)
            _trackedDuration = _playbackService.Duration;

        var playtimeMet = currentPlaytime >= PlaytimeThreshold;
        var percentageMet = _trackedDuration > 0
            && (currentPlaytime / _trackedDuration * 100) >= PercentageThreshold;

        var shouldScrobble = ConditionMode == ScrobbleConditionMode.Or
            ? playtimeMet || percentageMet
            : playtimeMet && percentageMet;

        if (shouldScrobble)
        {
            _hasScrobbled = true;
            _lastScrobbleTask = FireScrobbleAsync(_trackedTrackId, _trackedTitle);
        }
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        var isPlaying = _playbackService.IsPlaying;

        if (isPlaying && !_isSegmentActive && _trackedTrackId != null && IsEnabled)
        {
            _segmentStart = _timeProvider.GetTimestamp();
            _isSegmentActive = true;
            TrySendNowPlaying();
        }
        else if (!isPlaying && _isSegmentActive)
        {
            _accumulatedPlaytime += _timeProvider.GetElapsedTime(_segmentStart).TotalSeconds;
            _isSegmentActive = false;
        }
    }

    private void TrySendNowPlaying()
    {
        if (!_nowPlayingSent && NowPlayingEnabled && _trackedTrackId != null)
        {
            _nowPlayingSent = true;
            _ = EnqueueNowPlayingSendAsync(_trackedTrackId, _trackedTitle);
        }
    }

    private async Task FireScrobbleAsync(string trackId, string? title)
    {
        try
        {
            if (!IsEnabled) return;

            var client = _subsonicClientService.Client;
            if (client == null)
            {
                _logger.Debug("Scrobble skipped — no active connection", context: new { TrackId = trackId, Title = title });
                return;
            }

            var time = (ulong)_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            await client.ScrobbleAsync(trackId, time, submission: true);
            _logger.Debug("Scrobble submitted", context: new { TrackId = trackId, Title = title });
        }
        catch (Exception ex)
        {
            _logger.Error("Scrobble submission failed", context: new { TrackId = trackId, Title = title }, exception: ex);
        }
    }

    private Task EnqueueNowPlayingSendAsync(string trackId, string? title)
    {
        _pendingNowPlayingTrackId = trackId;

        _nowPlayingSendTask = _nowPlayingSendTask.ContinueWith(async _ =>
        {
            if (!IsEnabled || !NowPlayingEnabled) return;

            var current = _pendingNowPlayingTrackId;
            if (current != trackId) return;

            try
            {
                var client = _subsonicClientService.Client;
                if (client == null)
                {
                    _logger.Debug("Now Playing skipped — no active connection", context: new { TrackId = trackId, Title = title });
                    return;
                }

                await client.ScrobbleAsync(current, submission: false);
                _logger.Debug("Now Playing sent", context: new { TrackId = trackId, Title = title });
            }
            catch (Exception ex)
            {
                _logger.Error("Now Playing notification failed", context: new { TrackId = current, Title = title }, exception: ex);
            }
        }, TaskScheduler.Default).Unwrap();

        return _nowPlayingSendTask;
    }

    private async Task SaveSettingSafeAsync<T>(string key, T value)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync(key, value);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to save setting '{key}'", context: new { key, value }, exception: ex);
        }
    }
}
