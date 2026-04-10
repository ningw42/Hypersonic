using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Core.Services;

public sealed class LoggingService : ILoggingService, IDisposable
{
    private readonly List<SinkRegistration> _sinks = [];
    private readonly Lock _sinksLock = new();

    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public bool IsEnabled(LogLevel level)
    {
        if (level < MinimumLevel)
        {
            return false;
        }

        lock (_sinksLock)
        {
            foreach (var registration in _sinks)
            {
                if (level >= registration.MinimumLevel)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void AddSink(ILogSink sink, LogLevel minimumLevel = LogLevel.Debug)
    {
        lock (_sinksLock)
        {
            _sinks.Add(new SinkRegistration(sink, minimumLevel));
        }
    }

    public void RemoveSink(ILogSink sink)
    {
        lock (_sinksLock)
        {
            _sinks.RemoveAll(r => r.Sink == sink);
        }
    }

    public ILogger GetLogger(string category) => new CategoryLogger(this, category);

    internal void Dispatch(LogEntry entry)
    {
        if (entry.Level < MinimumLevel)
        {
            return;
        }

        SinkRegistration[] snapshot;
        lock (_sinksLock)
        {
            snapshot = [.. _sinks];
        }

        foreach (var registration in snapshot)
        {
            if (entry.Level >= registration.MinimumLevel)
            {
                try
                {
                    registration.Sink.Write(entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LoggingService] Sink {registration.Sink.GetType().Name} failed: {ex}");
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_sinksLock)
        {
            foreach (var registration in _sinks)
            {
                (registration.Sink as IDisposable)?.Dispose();
            }

            _sinks.Clear();
        }
    }

    private sealed record SinkRegistration(ILogSink Sink, LogLevel MinimumLevel);
}

internal sealed class CategoryLogger(LoggingService service, string category) : ILogger
{
    public string Category => category;

    public bool IsEnabled(LogLevel level) => service.IsEnabled(level);

    public void Log(LogLevel level, string message, object? context = null, Exception? exception = null)
    {
        service.Dispatch(new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message,
            Context = context,
            Exception = exception,
        });
    }

    public void Debug(string message, object? context = null)
        => Log(LogLevel.Debug, message, context);

    public void Info(string message, object? context = null)
        => Log(LogLevel.Info, message, context);

    public void Warning(string message, object? context = null, Exception? exception = null)
        => Log(LogLevel.Warning, message, context, exception);

    public void Error(string message, object? context = null, Exception? exception = null)
        => Log(LogLevel.Error, message, context, exception);
}
