using System.Collections.Concurrent;

using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Tests.Services.Cache;

/// <summary>
/// Test <see cref="ILoggingService"/> that records every warning message routed
/// through any logger it hands out. Used by the folder-migrator tests to assert that
/// conflict / preflight / move-failure paths emit a warning, without coupling to
/// exact message text.
/// </summary>
internal sealed class RecordingLoggingService : ILoggingService
{
    private readonly ConcurrentQueue<string> _warnings = new();

    public LogLevel MinimumLevel { get; set; }

    public IReadOnlyCollection<string> Warnings => _warnings.ToArray();

    public bool IsEnabled(LogLevel level) => true;

    public void AddSink(ILogSink sink, LogLevel minimumLevel = LogLevel.Debug) { }

    public void RemoveSink(ILogSink sink) { }

    public ILogger GetLogger(string category) => new RecordingLogger(_warnings, category);

    private sealed class RecordingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _warnings;

        public RecordingLogger(ConcurrentQueue<string> warnings, string category)
        {
            _warnings = warnings;
            Category = category;
        }

        public string Category { get; }

        public bool IsEnabled(LogLevel level) => true;

        public void Log(LogLevel level, string message, object? context = null, Exception? exception = null)
        {
            if (level == LogLevel.Warning)
            {
                _warnings.Enqueue(message);
            }
        }

        public void Debug(string message, object? context = null) { }

        public void Info(string message, object? context = null) { }

        public void Warning(string message, object? context = null, Exception? exception = null) => _warnings.Enqueue(message);

        public void Error(string message, object? context = null, Exception? exception = null) { }
    }
}
