using Hypersonic.Contracts.Services;
using Hypersonic.Models;

namespace Hypersonic.Services.Logging;

public sealed class DebugSink : ILogSink
{
    public void Write(LogEntry entry)
    {
        System.Diagnostics.Debug.WriteLine(Format(entry));
    }

    internal static string Format(LogEntry entry)
    {
        var line = $"{entry.Timestamp:O} [{entry.Level}] {entry.Category} — {entry.Message}";

        if (entry.Context is not null)
        {
            line += $" {entry.Context}";
        }

        if (entry.Exception is not null)
        {
            line += $" {entry.Exception}";
        }

        return line;
    }
}
