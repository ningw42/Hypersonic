using Hypersonic.Core.Models;

namespace Hypersonic.Core.Contracts.Services;

/// <summary>
/// A category-scoped logger obtained from ILoggingService.
/// </summary>
public interface ILogger
{
    string Category { get; }

    bool IsEnabled(LogLevel level);

    void Log(LogLevel level, string message, object? context = null, Exception? exception = null);

    void Debug(string message, object? context = null);
    void Info(string message, object? context = null);
    void Warning(string message, object? context = null, Exception? exception = null);
    void Error(string message, object? context = null, Exception? exception = null);
}
