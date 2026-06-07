using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Tests.Fakes;

public sealed class NullLogger : ILogger
{
    public string Category => string.Empty;
    public bool IsEnabled(LogLevel level) => false;
    public void Log(LogLevel level, string message, object? context = null, Exception? exception = null) { }
    public void Debug(string message, object? context = null) { }
    public void Info(string message, object? context = null) { }
    public void Warning(string message, object? context = null, Exception? exception = null) { }
    public void Error(string message, object? context = null, Exception? exception = null) { }
}

public sealed class NullLoggingService : ILoggingService
{
    public LogLevel MinimumLevel { get; set; }
    public bool IsEnabled(LogLevel level) => false;
    public void AddSink(ILogSink sink, LogLevel minimumLevel = LogLevel.Debug) { }
    public void RemoveSink(ILogSink sink) { }
    public ILogger GetLogger(string category) => new NullLogger();
}
