# Logging Service — Feature Spec & Design Spec

## Context

Hypersonic currently has no structured diagnostic logging. The only output is a handful of `System.Diagnostics.Debug.WriteLine()` calls in `App.xaml.cs` (unhandled exceptions) and `PlaybackService` (media failures). These are invisible in release builds and carry no level, timestamp, or structured context.

A lightweight, leveled, structured logging service will:

1. Provide a **single interface** for all diagnostic output across the app.
2. Support **multiple sinks** (console/debug output, rolling log files, and future sinks) running concurrently.
3. Carry **structured context** alongside every message — not just a formatted string.
4. Be **configurable** at runtime: which sinks are active, and the minimum level each sink accepts.

**Non-goal**: This is an application-level diagnostic logger for development and field troubleshooting. It is not a telemetry pipeline, crash reporter, or analytics system.

---

## Part 1: Feature Spec

### 1.1 Log Levels

Four levels, in ascending severity:

| Level | Intended use |
|---|---|
| `Debug` | Verbose development-time tracing (API call details, state transitions, timing). Typically disabled in release. |
| `Info` | Notable operational events (service initialized, server connected, playback started). |
| `Warning` | Recoverable problems (network retry, missing cover art, unexpected API response shape). |
| `Error` | Failures that impact functionality (authentication failure, media pipeline error, unhandled exception). |

### 1.2 Structured Context

Every log call accepts an optional context object of **any type**. Sinks serialize or inspect this context alongside the message. This enables post-hoc filtering and correlation without baking structured fields into the message string.

Examples:

```csharp
_log.Info("Playback started", new { TrackId = track.Id, Album = track.Album });
_log.Error("API call failed", new { Endpoint = "/rest/getAlbum", StatusCode = 500, ElapsedMs = 342 });
_log.Warning("Cover art missing, using fallback", new { AlbumId = id });
```

Sinks decide how to render context — `Debug` sink may call `ToString()`, file sink may serialize to JSON inline.

### 1.3 Logger Categories

Each logger instance carries a **category** string (typically the fully-qualified type name of the caller). This enables per-category filtering and helps locate the source of a log entry.

```csharp
// In PlaybackService constructor
_log = logService.GetLogger(nameof(PlaybackService));

// In AlbumDetailViewModel constructor
_log = logService.GetLogger(nameof(AlbumDetailViewModel));
```

### 1.4 Sinks

A **sink** is a destination for log entries. Multiple sinks can be active simultaneously, each with its own minimum level.

**Built-in sinks** (shipped with v1):

| Sink | Output | Default level | Notes |
|---|---|---|---|
| `DebugSink` | `System.Diagnostics.Debug.WriteLine` | `Debug` | Visible in VS Output window. No-op in release when no debugger attached. |
| `FileSink` | Per-launch text file in app local data folder | `Info` | One file per app launch, `hypersonic-yyyyMMdd-HHmmss.log`. UTF-8, one entry per line. |

**Future sinks** (not in scope, but the design must accommodate): in-app diagnostic overlay, remote logging endpoint, Windows Event Log.

### 1.5 Configuration

Sinks are registered at startup in `App.xaml.cs` as part of DI configuration. Each sink specifies its minimum level at registration time.

```csharp
services.AddSingleton<ILoggingService>(sp =>
{
    var service = new LoggingService();
    service.AddSink(new DebugSink(), LogLevel.Debug);
    service.AddSink(new FileSink(logDirectory), LogLevel.Info);
    return service;
});
```

A global minimum level can also be set on the service itself, acting as a gate before any sink is consulted. This allows suppressing all `Debug` output in release builds without removing sink registrations.

### 1.6 Sink Failure Policy

A log call must never crash the application. `LoggingService.Dispatch` catches exceptions from individual sinks, so a failing sink (disk full, serialization error, etc.) does not prevent later sinks from running and does not propagate to the caller.

- On sink failure, the exception is written to `System.Diagnostics.Debug.WriteLine` as a last-resort diagnostic (visible in debugger, no risk of recursion since it bypasses the logging pipeline).
- The failing entry is silently dropped for that sink.
- No retry — the next log call will attempt the sink again naturally.

This policy applies to both the `Write` call and any context serialization that happens inside a sink.

### 1.7 Thread Safety

Log calls may originate from UI threads, background tasks, and timer callbacks. All sink writes must be thread-safe. The `FileSink` serializes writes through a lock or channel to avoid interleaving.

### 1.8 Performance

- **Level guard + `IsEnabled`**: `Dispatch` checks the global level before iterating sinks. `ILogger` also exposes `IsEnabled(LogLevel)` so callers can skip expensive context construction entirely when a level is known to be suppressed:
  ```csharp
  if (_log.IsEnabled(LogLevel.Debug))
  {
      _log.Debug("Seek completed", new { Position = ComputeExpensiveDiagnostic() });
  }
  ```
  Without the guard, the caller still pays for argument evaluation (anonymous object allocation, method calls in initializers) even when the entry will be discarded. `IsEnabled` returns `false` when the level is below the global minimum, making the cost a single comparison in the filtered-out case.
- **No async in the hot path**: Log methods are synchronous (fire-and-forget to sinks). `FileSink` may buffer internally but the caller never awaits.
- **Context serialization is lazy**: Sinks that need JSON (e.g., `FileSink`) serialize the context object; sinks that don't (e.g., `DebugSink`) can skip it.

### 1.8 Log Entry Format

Each entry carries:

| Field | Type | Description |
|---|---|---|
| `Timestamp` | `DateTime` | UTC time of the log call |
| `Level` | `LogLevel` | Severity |
| `Category` | `string` | Logger category (caller type name) |
| `Message` | `string` | Human-readable message |
| `Context` | `object?` | Optional structured data |
| `Exception` | `Exception?` | Optional captured exception |

**File sink line format**:

```
2026-03-27T14:05:32.123Z [INFO] PlaybackService — Playback started {"TrackId":"abc123","Album":"OK Computer"}
2026-03-27T14:05:33.456Z [ERROR] SubsonicClientService — API call failed {"Endpoint":"/rest/getAlbum","StatusCode":500} System.Net.Http.HttpRequestException: ...
```

### 1.9 Not In Scope

- Log rotation / size-based pruning (one file per launch for v1; user can clear old files manually).
- Remote log shipping / telemetry.
- Per-category level overrides (global + per-sink levels are sufficient initially).
- Async sinks or background log flushing (simplicity over throughput).
- Replacing `Microsoft.Extensions.Logging` — this is a standalone service following the project's existing patterns. An adapter to `ILogger<T>` can be added later if needed.

---

## Part 2: Design Spec

### 2.1 New Models

**`LogLevel`** — new file: `Hypersonic/Models/LogLevel.cs`

```csharp
namespace Hypersonic.Models;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}
```

Integer values enable `>=` comparison for level filtering.

**`LogEntry`** — new file: `Hypersonic/Models/LogEntry.cs`

```csharp
namespace Hypersonic.Models;

public sealed class LogEntry
{
    public required DateTime Timestamp { get; init; }
    public required LogLevel Level { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }
    public object? Context { get; init; }
    public Exception? Exception { get; init; }
}
```

### 2.2 Sink Abstraction

**`ILogSink`** — new file: `Hypersonic/Contracts/Services/ILogSink.cs`

```csharp
namespace Hypersonic.Contracts.Services;

public interface ILogSink
{
    void Write(LogEntry entry);
}
```

Single method, synchronous. Sinks are responsible for their own thread safety.

### 2.3 Logger Interface

**`ILogger`** — new file: `Hypersonic/Contracts/Services/ILogger.cs`

```csharp
namespace Hypersonic.Contracts.Services;

using Hypersonic.Models;

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
```

The convenience methods (`Debug`, `Info`, `Warning`, `Error`) delegate to `Log`. `Warning` and `Error` accept an optional `Exception` since those levels commonly accompany exceptions.

### 2.4 Service Interface

**`ILoggingService`** — new file: `Hypersonic/Contracts/Services/ILoggingService.cs`

```csharp
namespace Hypersonic.Contracts.Services;

using Hypersonic.Models;

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
```

### 2.5 Service Implementation

**`LoggingService`** — new file: `Hypersonic/Services/LoggingService.cs`

```csharp
namespace Hypersonic.Services;

using Hypersonic.Contracts.Services;
using Hypersonic.Models;

public sealed class LoggingService : ILoggingService, IDisposable
{
    private readonly List<SinkRegistration> _sinks = [];
    private readonly Lock _sinksLock = new();

    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public bool IsEnabled(LogLevel level)
    {
        if (level < MinimumLevel)
        {
            return false;
        }

        lock (_sinksLock)
        {
            foreach (var registration in _sinks)
            {
                if (level >= registration.MinimumLevel)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void AddSink(ILogSink sink, LogLevel minimumLevel = LogLevel.Debug)
    {
        lock (_sinksLock)
        {
            _sinks.Add(new SinkRegistration(sink, minimumLevel));
        }
    }

    public void RemoveSink(ILogSink sink)
    {
        lock (_sinksLock)
        {
            _sinks.RemoveAll(r => r.Sink == sink);
        }
    }

    public ILogger GetLogger(string category) => new CategoryLogger(this, category);

    internal void Dispatch(LogEntry entry)
    {
        if (entry.Level < MinimumLevel)
        {
            return;
        }

        SinkRegistration[] snapshot;
        lock (_sinksLock)
        {
            snapshot = [.. _sinks];
        }

        foreach (var registration in snapshot)
        {
            if (entry.Level >= registration.MinimumLevel)
            {
                try
                {
                    registration.Sink.Write(entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LoggingService] Sink {registration.Sink.GetType().Name} failed: {ex}");
                }
            }
        }
    }

    private sealed record SinkRegistration(ILogSink Sink, LogLevel MinimumLevel);
}
```

**`CategoryLogger`** — nested or separate internal class:

```csharp
namespace Hypersonic.Services;

using Hypersonic.Contracts.Services;
using Hypersonic.Models;

internal sealed class CategoryLogger(LoggingService service, string category) : ILogger
{
    public string Category => category;

    public bool IsEnabled(LogLevel level) => service.IsEnabled(level);

    public void Log(LogLevel level, string message, object? context = null, Exception? exception = null)
    {
        service.Dispatch(new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message,
            Context = context,
            Exception = exception,
        });
    }

    public void Debug(string message, object? context = null)
        => Log(LogLevel.Debug, message, context);

    public void Info(string message, object? context = null)
        => Log(LogLevel.Info, message, context);

    public void Warning(string message, object? context = null, Exception? exception = null)
        => Log(LogLevel.Warning, message, context, exception);

    public void Error(string message, object? context = null, Exception? exception = null)
        => Log(LogLevel.Error, message, context, exception);
}
```

### 2.6 Built-in Sinks

**`DebugSink`** — new file: `Hypersonic/Services/Logging/DebugSink.cs`

```csharp
namespace Hypersonic.Services.Logging;

using System.Diagnostics;
using Hypersonic.Contracts.Services;
using Hypersonic.Models;

public sealed class DebugSink : ILogSink
{
    public void Write(LogEntry entry)
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

        System.Diagnostics.Debug.WriteLine(line);
    }
}
```

`DebugSink` uses `ToString()` for context — cheap and adequate for the debugger output window. Anonymous types produce readable `{ TrackId = abc123, Album = OK Computer }` output.

**`FileSink`** — new file: `Hypersonic/Services/Logging/FileSink.cs`

```csharp
namespace Hypersonic.Services.Logging;

using Hypersonic.Contracts.Services;
using Hypersonic.Models;
using Newtonsoft.Json;

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
                _writer.Write(entry.Exception.ToString());
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
```

Key decisions:

- **One file per launch**: The file is created in the constructor with a timestamp suffix (`yyyyMMdd-HHmmss`). No date-rolling logic needed — each app session gets its own file, making it easy to correlate logs with a specific run.
- **Lock-based serialization**: Simple and correct. Log volume in a desktop music app is low enough that contention is negligible.
- **Flush after every entry**: Ensures log entries survive crashes. Acceptable for the expected low throughput.
- **Newtonsoft.Json for context**: Reuses the project's existing dependency for structured context serialization.
- **`IDisposable`**: `FileSink` owns a file handle. Since sinks are created inside the `ILoggingService` factory (not registered individually with DI), `LoggingService` owns their lifecycle and disposes all `IDisposable` sinks when it is itself disposed (see Section 2.7).

### 2.7 DI Registration & Lifecycle

In `App.xaml.cs`, add to the `ConfigureServices` block:

```csharp
// Logging
services.AddSingleton<ILoggingService>(sp =>
{
    var service = new LoggingService();
    service.AddSink(new DebugSink(), LogLevel.Debug);

    var logDir = RuntimeHelper.IsMSIX
        ? Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hypersonic", "Logs");
    service.AddSink(new FileSink(logDir), LogLevel.Info);

#if !DEBUG
    service.MinimumLevel = LogLevel.Info;
#endif

    return service;
});
```

- Debug builds: all levels, both sinks.
- Release builds: global minimum `Info`, so `Debug` entries are suppressed before any sink sees them.
- **MSIX path**: `ApplicationData.Current.LocalFolder\Logs\` (managed by Windows, cleaned on uninstall).
- **Unpackaged path**: `%LOCALAPPDATA%\Hypersonic\Logs\` (matches the existing `LocalSettingsService` convention).

**FileSink disposal**: The `FileSink` is created inside the `ILoggingService` factory lambda, so the DI container does not track it for disposal. The `App.xaml.cs` `OnLaunched` / window-closed handler must dispose disposable sinks explicitly. A straightforward approach:

```csharp
// In MainWindow.Closed or App shutdown path
if (App.GetService<ILoggingService>() is LoggingService loggingService)
{
    loggingService.Dispose();
}
```

To support this, `LoggingService` implements `IDisposable` and disposes all sinks that are themselves `IDisposable`:

```csharp
public sealed class LoggingService : ILoggingService, IDisposable
{
    // ... existing members ...

    public void Dispose()
    {
        lock (_sinksLock)
        {
            foreach (var registration in _sinks)
            {
                (registration.Sink as IDisposable)?.Dispose();
            }

            _sinks.Clear();
        }
    }
}
```

This keeps sink lifetime management centralized — callers register sinks and the service owns tearing them down.

### 2.8 Consumer Usage Pattern

Services and ViewModels obtain a logger via constructor injection:

```csharp
public class PlaybackService : IPlaybackService
{
    private readonly ILogger _log;

    public PlaybackService(ILoggingService logService)
    {
        _log = logService.GetLogger(nameof(PlaybackService));
    }

    public void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null)
    {
        _log.Info("Queue replaced", new { TrackCount = tracks.Count, StartIndex = startIndex, Source = source?.Name });
        // ...
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _log.Error("Media playback failed", new
        {
            ErrorCode = args.ExtendedErrorCode.HResult,
            ErrorMessage = args.ErrorMessage,
            TrackId = NowPlaying?.Id,
        });
    }
}
```

### 2.9 File & Folder Layout

```
Hypersonic/
├── Contracts/Services/
│   ├── ILogSink.cs          (new)
│   ├── ILogger.cs           (new)
│   └── ILoggingService.cs   (new)
├── Models/
│   ├── LogEntry.cs           (new)
│   └── LogLevel.cs           (new)
├── Services/
│   ├── LoggingService.cs     (new — LoggingService + CategoryLogger)
│   └── Logging/
│       ├── DebugSink.cs      (new)
│       └── FileSink.cs       (new)
```

### 2.10 Extensibility

Adding a new sink requires:

1. Implement `ILogSink.Write(LogEntry)`.
2. Register it via `service.AddSink(new MySink(), LogLevel.Whatever)` at startup.

No changes to `ILoggingService`, `ILogger`, or existing sinks. Examples of future sinks:

- **InAppSink** — buffers recent entries for an in-app diagnostic overlay.
- **EventLogSink** — writes to Windows Event Log for production diagnostics.
- **RemoteSink** — posts batched entries to an HTTP endpoint.

---

## Part 3: Implementation Sequence

1. **Models**: Add `LogLevel` and `LogEntry`.
2. **Contracts**: Add `ILogSink`, `ILogger`, `ILoggingService`.
3. **Service**: Implement `LoggingService` and `CategoryLogger`.
4. **Sinks**: Implement `DebugSink` and `FileSink`.
5. **DI**: Register `ILoggingService` in `App.xaml.cs`.
6. **Adopt**: Replace `Debug.WriteLine` calls in `App.xaml.cs` and `PlaybackService` with logger calls.
7. **Test**: Unit tests for level filtering, sink dispatch, `FileSink` per-launch file creation.

---

## Part 4: Test Plan

| Test | Verifies |
|---|---|
| Log below global minimum is not dispatched | `LoggingService.MinimumLevel` gating |
| Log below sink minimum is not written | Per-sink level filtering |
| Log at or above both levels reaches the sink | Happy path dispatch |
| Multiple sinks each receive qualifying entries | Fan-out behavior |
| `RemoveSink` stops delivery to that sink | Sink lifecycle |
| `CategoryLogger` populates category and timestamp | Entry construction |
| `IsEnabled` returns false when below global minimum | Guard check accuracy |
| `IsEnabled` returns true when at least one sink accepts the level | Guard check with mixed sink levels |
| Sink exception does not propagate to caller | Sink failure isolation |
| Sink exception does not prevent later sinks from receiving the entry | Sink failure does not break fan-out |
| `FileSink` creates file with launch timestamp in name | Per-launch file naming |
| `FileSink` serializes context as JSON | Structured context output |
| `FileSink` appends exception string | Exception formatting |
| `DebugSink` formats entry as expected string | Debug output format |
| `LoggingService.Dispose` disposes all `IDisposable` sinks | Lifecycle cleanup |
| Concurrent log calls do not corrupt file output | Thread safety |
