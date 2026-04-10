using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Logging;

namespace Hypersonic.Core.Tests.Services;

[TestClass]
public class LoggingServiceTest
{
    private sealed class SpySink : ILogSink
    {
        public List<LogEntry> Entries { get; } = [];

        public void Write(LogEntry entry) => Entries.Add(entry);
    }

    private sealed class ThrowingSink : ILogSink
    {
        public void Write(LogEntry entry) => throw new InvalidOperationException("Boom");
    }

    private sealed class DisposableSpySink : ILogSink, IDisposable
    {
        public bool Disposed { get; private set; }
        public List<LogEntry> Entries { get; } = [];

        public void Write(LogEntry entry) => Entries.Add(entry);
        public void Dispose() => Disposed = true;
    }

    // --- Level filtering ---

    [TestMethod]
    public void LogBelowGlobalMinimum_IsNotDispatched()
    {
        var service = new LoggingService { MinimumLevel = LogLevel.Warning };
        var sink = new SpySink();
        service.AddSink(sink, LogLevel.Debug);

        var log = service.GetLogger("Test");
        log.Debug("should be dropped");
        log.Info("should also be dropped");

        Assert.AreEqual(0, sink.Entries.Count);
    }

    [TestMethod]
    public void LogBelowSinkMinimum_IsNotWritten()
    {
        var service = new LoggingService();
        var sink = new SpySink();
        service.AddSink(sink, LogLevel.Warning);

        var log = service.GetLogger("Test");
        log.Debug("dropped");
        log.Info("dropped");

        Assert.AreEqual(0, sink.Entries.Count);
    }

    [TestMethod]
    public void LogAtOrAboveBothLevels_ReachesSink()
    {
        var service = new LoggingService { MinimumLevel = LogLevel.Info };
        var sink = new SpySink();
        service.AddSink(sink, LogLevel.Info);

        var log = service.GetLogger("Test");
        log.Info("should arrive");
        log.Warning("should arrive");
        log.Error("should arrive");

        Assert.AreEqual(3, sink.Entries.Count);
    }

    // --- Fan-out ---

    [TestMethod]
    public void MultipleSinks_EachReceiveQualifyingEntries()
    {
        var service = new LoggingService();
        var debugSink = new SpySink();
        var infoSink = new SpySink();
        service.AddSink(debugSink, LogLevel.Debug);
        service.AddSink(infoSink, LogLevel.Info);

        var log = service.GetLogger("Test");
        log.Debug("debug only");
        log.Info("both sinks");

        Assert.AreEqual(2, debugSink.Entries.Count);
        Assert.AreEqual(1, infoSink.Entries.Count);
        Assert.AreEqual("both sinks", infoSink.Entries[0].Message);
    }

    // --- RemoveSink ---

    [TestMethod]
    public void RemoveSink_StopsDelivery()
    {
        var service = new LoggingService();
        var sink = new SpySink();
        service.AddSink(sink);

        var log = service.GetLogger("Test");
        log.Info("before removal");
        Assert.AreEqual(1, sink.Entries.Count);

        service.RemoveSink(sink);
        log.Info("after removal");
        Assert.AreEqual(1, sink.Entries.Count);
    }

    // --- CategoryLogger ---

    [TestMethod]
    public void CategoryLogger_PopulatesCategoryAndTimestamp()
    {
        var service = new LoggingService();
        var sink = new SpySink();
        service.AddSink(sink);

        var before = DateTime.UtcNow;
        var log = service.GetLogger("MyCategory");
        log.Info("test message");
        var after = DateTime.UtcNow;

        Assert.AreEqual(1, sink.Entries.Count);
        var entry = sink.Entries[0];
        Assert.AreEqual("MyCategory", entry.Category);
        Assert.AreEqual(LogLevel.Info, entry.Level);
        Assert.AreEqual("test message", entry.Message);
        Assert.IsTrue(entry.Timestamp >= before && entry.Timestamp <= after);
    }

    [TestMethod]
    public void CategoryLogger_Category_MatchesConstructorArgument()
    {
        var service = new LoggingService();
        var log = service.GetLogger("SomeService");
        Assert.AreEqual("SomeService", log.Category);
    }

    // --- IsEnabled ---

    [TestMethod]
    public void IsEnabled_ReturnsFalse_WhenBelowGlobalMinimum()
    {
        var service = new LoggingService { MinimumLevel = LogLevel.Warning };
        var sink = new SpySink();
        service.AddSink(sink, LogLevel.Debug);

        Assert.IsFalse(service.IsEnabled(LogLevel.Debug));
        Assert.IsFalse(service.IsEnabled(LogLevel.Info));
    }

    [TestMethod]
    public void IsEnabled_ReturnsTrue_WhenAtLeastOneSinkAccepts()
    {
        var service = new LoggingService();
        var sink = new SpySink();
        service.AddSink(sink, LogLevel.Warning);

        Assert.IsFalse(service.IsEnabled(LogLevel.Debug));
        Assert.IsFalse(service.IsEnabled(LogLevel.Info));
        Assert.IsTrue(service.IsEnabled(LogLevel.Warning));
        Assert.IsTrue(service.IsEnabled(LogLevel.Error));
    }

    [TestMethod]
    public void ILoggerIsEnabled_DelegatesToService()
    {
        var service = new LoggingService { MinimumLevel = LogLevel.Warning };
        var sink = new SpySink();
        service.AddSink(sink, LogLevel.Warning);

        var log = service.GetLogger("Test");
        Assert.IsFalse(log.IsEnabled(LogLevel.Debug));
        Assert.IsFalse(log.IsEnabled(LogLevel.Info));
        Assert.IsTrue(log.IsEnabled(LogLevel.Warning));
        Assert.IsTrue(log.IsEnabled(LogLevel.Error));
    }

    // --- Sink failure isolation ---

    [TestMethod]
    public void SinkException_DoesNotPropagateToCall()
    {
        var service = new LoggingService();
        service.AddSink(new ThrowingSink());

        var log = service.GetLogger("Test");
        // Should not throw.
        log.Error("this should not crash");
    }

    [TestMethod]
    public void SinkException_DoesNotPreventLaterSinks()
    {
        var service = new LoggingService();
        service.AddSink(new ThrowingSink());
        var laterSink = new SpySink();
        service.AddSink(laterSink);

        var log = service.GetLogger("Test");
        log.Error("should reach later sink");

        Assert.AreEqual(1, laterSink.Entries.Count);
    }

    // --- Dispose ---

    [TestMethod]
    public void Dispose_DisposesAllDisposableSinks()
    {
        var service = new LoggingService();
        var disposableSink = new DisposableSpySink();
        var normalSink = new SpySink();
        service.AddSink(disposableSink);
        service.AddSink(normalSink);

        service.Dispose();

        Assert.IsTrue(disposableSink.Disposed);
    }

    // --- Convenience methods ---

    [TestMethod]
    public void DebugMethod_SetsLevelToDebug()
    {
        var service = new LoggingService();
        var sink = new SpySink();
        service.AddSink(sink);

        var log = service.GetLogger("Test");
        log.Debug("msg", new { Key = "val" });

        Assert.AreEqual(1, sink.Entries.Count);
        Assert.AreEqual(LogLevel.Debug, sink.Entries[0].Level);
        Assert.IsNotNull(sink.Entries[0].Context);
    }

    [TestMethod]
    public void WarningMethod_SetsLevelAndException()
    {
        var service = new LoggingService();
        var sink = new SpySink();
        service.AddSink(sink);

        var ex = new InvalidOperationException("test");
        var log = service.GetLogger("Test");
        log.Warning("warn", exception: ex);

        Assert.AreEqual(LogLevel.Warning, sink.Entries[0].Level);
        Assert.AreEqual(ex, sink.Entries[0].Exception);
    }

    [TestMethod]
    public void ErrorMethod_SetsLevelAndException()
    {
        var service = new LoggingService();
        var sink = new SpySink();
        service.AddSink(sink);

        var ex = new InvalidOperationException("test");
        var log = service.GetLogger("Test");
        log.Error("err", exception: ex);

        Assert.AreEqual(LogLevel.Error, sink.Entries[0].Level);
        Assert.AreEqual(ex, sink.Entries[0].Exception);
    }
}

[TestClass]
public class FileSinkTest
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hypersonic-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void FileSink_CreatesFileWithLaunchTimestamp()
    {
        using var sink = new FileSink(_tempDir);

        var files = Directory.GetFiles(_tempDir, "hypersonic-*.log");
        Assert.AreEqual(1, files.Length);
        Assert.IsTrue(Path.GetFileName(files[0]).StartsWith("hypersonic-"));
        Assert.IsTrue(files[0].EndsWith(".log"));
    }

    [TestMethod]
    public void FileSink_SerializesContextAsJson()
    {
        var sink = new FileSink(_tempDir);

        sink.Write(new LogEntry
        {
            Timestamp = new DateTime(2026, 3, 27, 14, 5, 32, 123, DateTimeKind.Utc),
            Level = LogLevel.Info,
            Category = "TestCategory",
            Message = "Test message",
            Context = new { TrackId = "abc123", Album = "OK Computer" },
        });

        sink.Dispose();

        var files = Directory.GetFiles(_tempDir, "*.log");
        var content = File.ReadAllText(files[0]);
        Assert.IsTrue(content.Contains("[INFO]"));
        Assert.IsTrue(content.Contains("TestCategory"));
        Assert.IsTrue(content.Contains("Test message"));
        Assert.IsTrue(content.Contains("\"TrackId\":\"abc123\""));
        Assert.IsTrue(content.Contains("\"Album\":\"OK Computer\""));
    }

    [TestMethod]
    public void FileSink_AppendsExceptionString()
    {
        var sink = new FileSink(_tempDir);

        var ex = new InvalidOperationException("Something broke");
        sink.Write(new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Error,
            Category = "Test",
            Message = "Failure",
            Exception = ex,
        });

        sink.Dispose();

        var files = Directory.GetFiles(_tempDir, "*.log");
        var content = File.ReadAllText(files[0]);
        Assert.IsTrue(content.Contains("[ERROR]"));
        Assert.IsTrue(content.Contains("Something broke"));
    }

    [TestMethod]
    public void FileSink_ExceptionNewlines_EscapedToSingleLine()
    {
        var sink = new FileSink(_tempDir);

        Exception captured;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        sink.Write(new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Error,
            Category = "Test",
            Message = "Failure",
            Exception = captured,
        });

        sink.Dispose();

        var files = Directory.GetFiles(_tempDir, "*.log");
        var lines = File.ReadAllLines(files[0]);
        Assert.AreEqual(1, lines.Length);
        Assert.IsTrue(lines[0].Contains("\\n"));
    }

    [TestMethod]
    public void FileSink_ConcurrentWrites_DoNotCorrupt()
    {
        var sink = new FileSink(_tempDir);
        var entries = Enumerable.Range(0, 100).Select(i => new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Info,
            Category = "Concurrent",
            Message = $"Entry {i}",
        }).ToList();

        Parallel.ForEach(entries, entry => sink.Write(entry));

        sink.Dispose();

        var files = Directory.GetFiles(_tempDir, "*.log");
        var lines = File.ReadAllLines(files[0]);
        Assert.AreEqual(100, lines.Length);
    }
}

[TestClass]
public class DebugSinkTest
{
    [TestMethod]
    public void Format_BasicEntry_ContainsAllFields()
    {
        var result = DebugSink.Format(new LogEntry
        {
            Timestamp = new DateTime(2026, 3, 27, 14, 5, 32, 123, DateTimeKind.Utc),
            Level = LogLevel.Info,
            Category = "TestService",
            Message = "Something happened",
        });

        Assert.AreEqual("2026-03-27T14:05:32.1230000Z [Info] TestService — Something happened", result);
    }

    [TestMethod]
    public void Format_WithContext_AppendsToString()
    {
        var result = DebugSink.Format(new LogEntry
        {
            Timestamp = new DateTime(2026, 3, 27, 14, 5, 32, 123, DateTimeKind.Utc),
            Level = LogLevel.Debug,
            Category = "TestService",
            Message = "With context",
            Context = new { Key = "value" },
        });

        Assert.IsTrue(result.Contains("With context"));
        Assert.IsTrue(result.Contains("Key = value"));
    }

    [TestMethod]
    public void Format_WithException_AppendsExceptionString()
    {
        var ex = new InvalidOperationException("test error");
        var result = DebugSink.Format(new LogEntry
        {
            Timestamp = new DateTime(2026, 3, 27, 14, 5, 32, 123, DateTimeKind.Utc),
            Level = LogLevel.Error,
            Category = "TestService",
            Message = "With exception",
            Exception = ex,
        });

        Assert.IsTrue(result.Contains("[Error]"));
        Assert.IsTrue(result.Contains("With exception"));
        Assert.IsTrue(result.Contains("InvalidOperationException: test error"));
    }

    [TestMethod]
    public void Format_WithContextAndException_ContainsBoth()
    {
        var ex = new ArgumentException("arg error");
        var result = DebugSink.Format(new LogEntry
        {
            Timestamp = new DateTime(2026, 3, 27, 14, 5, 32, 123, DateTimeKind.Utc),
            Level = LogLevel.Warning,
            Category = "TestService",
            Message = "Full entry",
            Context = new { Detail = 42 },
            Exception = ex,
        });

        Assert.IsTrue(result.Contains("Detail = 42"));
        Assert.IsTrue(result.Contains("ArgumentException: arg error"));
    }
}
