namespace Hypersonic.Core.Models;

public sealed class LogEntry
{
    public required DateTime Timestamp { get; init; }
    public required LogLevel Level { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }
    public object? Context { get; init; }
    public Exception? Exception { get; init; }
}
