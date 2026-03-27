using Hypersonic.Models;

namespace Hypersonic.Contracts.Services;

public interface ILoggingService
{
    /// <summary>
    /// The global minimum level. Log calls below this are discarded before reaching any sink.
    /// </summary>
    LogLevel MinimumLevel { get; set; }

    /// <summary>
    /// Returns true if any sink would accept an entry at this level.
    /// </summary>
    bool IsEnabled(LogLevel level);

    /// <summary>
    /// Register a sink with its own minimum level threshold.
    /// </summary>
    void AddSink(ILogSink sink, LogLevel minimumLevel = LogLevel.Debug);

    /// <summary>
    /// Remove a previously registered sink.
    /// </summary>
    void RemoveSink(ILogSink sink);

    /// <summary>
    /// Create a category-scoped logger.
    /// </summary>
    ILogger GetLogger(string category);
}
