using Hypersonic.Contracts.Services;
using Hypersonic.Models;
using Newtonsoft.Json;

namespace Hypersonic.Services.Logging;

public sealed class FileSink : ILogSink, IDisposable
{
    private readonly Lock _writeLock = new();
    private readonly StreamWriter _writer;

    public FileSink(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(logDirectory, $"hypersonic-{timestamp}.log");
        _writer = new StreamWriter(path, append: false, encoding: System.Text.Encoding.UTF8);
    }

    public void Write(LogEntry entry)
    {
        lock (_writeLock)
        {
            _writer.Write(entry.Timestamp.ToString("O"));
            _writer.Write(" [");
            _writer.Write(entry.Level.ToString().ToUpperInvariant());
            _writer.Write("] ");
            _writer.Write(entry.Category);
            _writer.Write(" — ");
            _writer.Write(entry.Message);

            if (entry.Context is not null)
            {
                _writer.Write(' ');
                _writer.Write(JsonConvert.SerializeObject(entry.Context));
            }

            if (entry.Exception is not null)
            {
                _writer.Write(' ');
                _writer.Write(entry.Exception.ToString().ReplaceLineEndings("\\n"));
            }

            _writer.WriteLine();
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            _writer.Dispose();
        }
    }
}
