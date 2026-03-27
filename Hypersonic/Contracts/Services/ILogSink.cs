using Hypersonic.Models;

namespace Hypersonic.Contracts.Services;

public interface ILogSink
{
    void Write(LogEntry entry);
}
