using Hypersonic.Core.Models;

namespace Hypersonic.Core.Contracts.Services;

public interface ILogSink
{
    void Write(LogEntry entry);
}
