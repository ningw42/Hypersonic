# Logging Service — Implementation Details

Companion to [docs/design/service/logging-service.md](../../design/service/logging-service.md). This document records what was actually built, decisions made during implementation, and deviations from the original design.

---

## Files Changed

### New files

| File | Purpose |
|---|---|
| `Hypersonic/Models/LogLevel.cs` | `Debug`, `Info`, `Warning`, `Error` enum with integer values for `>=` comparison |
| `Hypersonic/Models/LogEntry.cs` | Sealed record-like class with `required` init properties: `Timestamp`, `Level`, `Category`, `Message`, plus optional `Context` and `Exception` |
| `Hypersonic/Contracts/Services/ILogSink.cs` | Single-method `Write(LogEntry)` sink interface |
| `Hypersonic/Contracts/Services/ILogger.cs` | Category-scoped logger: `Log`, `Debug`, `Info`, `Warning`, `Error` convenience methods, `IsEnabled` guard |
| `Hypersonic/Contracts/Services/ILoggingService.cs` | Service interface: `MinimumLevel`, `IsEnabled`, `AddSink`, `RemoveSink`, `GetLogger` |
| `Hypersonic/Services/LoggingService.cs` | `LoggingService` (dispatch, sink lifecycle, `IDisposable`) and `CategoryLogger` (internal, primary-constructor) |
| `Hypersonic/Services/Logging/DebugSink.cs` | Writes to `System.Diagnostics.Debug.WriteLine`, uses `ToString()` for context |
| `Hypersonic/Services/Logging/FileSink.cs` | Per-launch UTF-8 log file, JSON context via Newtonsoft.Json, lock-based thread safety |
| `Hypersonic.UnitTest/LoggingServiceTest.cs` | 25 unit tests across `LoggingServiceTest`, `FileSinkTest`, `DebugSinkTest` |

### Modified files

| File | Changes |
|---|---|
| `Hypersonic/App.xaml.cs` | Added `ILoggingService` DI registration with `DebugSink` + `FileSink`; replaced 3 `Debug.WriteLine` calls in `App_UnhandledException` with structured `_log.Error()` |
| `Hypersonic/Services/PlaybackService.cs` | Added `ILoggingService` constructor parameter; replaced `Debug.WriteLine` in `OnMediaFailed` with structured `_log.Error()` including `ErrorCode`, `ErrorMessage`, `TrackId` |
| `Hypersonic/MainWindow.xaml.cs` | Added `Closed` handler to dispose `LoggingService` on window close (flushes `FileSink`) |
| `Hypersonic.UnitTest/Hypersonic.UnitTest.csproj` | Added linked source files for logging types and `Newtonsoft.Json` package reference |

---

## Design Deviations

### FileSink exception newline escaping

The design doc specifies "one entry per line" for the file sink and shows `Exception.ToString()` appended directly. However, `Exception.ToString()` includes embedded newlines (stack traces), which would break the one-entry-per-line invariant. The implementation applies `.ReplaceLineEndings("\\n")` to collapse exception output into a single line with literal `\n` markers:

```csharp
_writer.Write(entry.Exception.ToString().ReplaceLineEndings("\\n"));
```

This preserves parseability — each line in the log file is exactly one entry — while keeping the full stack trace information accessible.

### FileSink creation failure isolation

The design doc registers `FileSink` directly in the DI factory without error handling. If directory or file creation fails (permissions, disk full, invalid path), the entire `ILoggingService` factory would throw, crashing startup before any logging is available.

The implementation wraps `FileSink` creation in a try/catch. On failure, it falls back to `Debug.WriteLine` and the service continues with `DebugSink` only:

```csharp
try
{
    var logDir = RuntimeHelper.IsMSIX
        ? Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hypersonic", "Logs");
    service.AddSink(new FileSink(logDir), LogLevel.Info);
}
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"[LoggingService] FileSink creation failed: {ex}");
}
```

This aligns with the design's stated principle that "a log call must never crash the application" — extended to sink construction as well.

### DebugSink.Format extracted as internal static method

The design doc places all formatting inline in `Write()`. The implementation extracts it into `internal static string Format(LogEntry)` so that unit tests can assert on the actual formatted string without needing to capture `Debug.WriteLine` output. `Write()` delegates to `Format()`:

```csharp
public void Write(LogEntry entry)
{
    System.Diagnostics.Debug.WriteLine(Format(entry));
}

internal static string Format(LogEntry entry)
{
    // ... formatting logic ...
}
```

No behavioral change — purely a testability improvement.

### MainWindow.Closed disposal handler

The design doc mentions disposing `LoggingService` "in MainWindow.Closed or App shutdown path" but does not specify which. The implementation uses `MainWindow.Closed` since it fires reliably on window close and the `MainWindow` instance is accessible via `App.MainWindow`:

```csharp
Closed += MainWindow_Closed;

private void MainWindow_Closed(object sender, WindowEventArgs args)
{
    if (App.GetService<ILoggingService>() is LoggingService loggingService)
    {
        loggingService.Dispose();
    }
}
```

The `is LoggingService` pattern-match avoids a hard cast while accessing the `Dispose()` method that only exists on the concrete type (not on `ILoggingService`).

---

## Implementation Notes

### Thread safety

- `LoggingService` uses `Lock` (not `object`) for the sink list, matching .NET 10 best practices.
- `Dispatch` takes a snapshot of the sink list under lock, then iterates outside the lock. This avoids holding the lock during potentially slow sink writes.
- `FileSink` uses a separate `Lock` for write serialization. Flush-after-every-write ensures entries survive crashes.
- `CategoryLogger` is stateless beyond its constructor arguments and delegates all work to `LoggingService`, so it is inherently thread-safe.

### CategoryLogger placement

`CategoryLogger` is defined as a top-level `internal sealed class` in `LoggingService.cs` (not nested). It uses a primary constructor and delegates all methods to the parent `LoggingService.Dispatch` / `IsEnabled`. This keeps it visible to the test project via linked source files while preventing external construction.

### Test project choice

Tests live in `Hypersonic.UnitTest` (targets `net10.0`) rather than `Hypersonic.Tests.MSTest` (targets `net10.0-windows10.0.19041.0`). The logging service has no WinUI dependencies, so using the plain .NET target avoids the Windows App SDK test host issues that affect `Hypersonic.Tests.MSTest`. Source files are included via `<Compile Include="..\..\Hypersonic\..." Link="..." />` links, following the existing pattern for `PlaybackQueue` tests.

### Existing Debug.WriteLine calls replaced

| Location | Before | After |
|---|---|---|
| `App.xaml.cs:App_UnhandledException` | 3 separate `Debug.WriteLine` calls for exception, message, stack trace | Single `log.Error("Unhandled exception", new { e.Message }, e.Exception)` |
| `PlaybackService.cs:OnMediaFailed` | `Debug.WriteLine($"Media failed: {args.ErrorMessage}")` | `_log.Error("Media playback failed", new { ErrorCode, ErrorMessage, TrackId })` |

The `PlaybackService` constructor now requires `ILoggingService`, injected by the DI container. No other constructor parameters were added.

---

## Verification Results

### Build

`dotnet build Hypersonic.sln -p:Platform=x64` — passed, 0 errors.

### Automated tests

| Test project | Result |
|---|---|
| `Hypersonic.UnitTest` | 80 passed (55 pre-existing PlaybackQueue + 25 new logging) |
| `Hypersonic.Client.UnitTest` | Not affected by this change |
| `Hypersonic.Tests.MSTest` | Blocked by pre-existing Windows App SDK test host issue — unrelated to this change |

### Test coverage

| Test class | Count | Covers |
|---|---|---|
| `LoggingServiceTest` | 16 | Global level gating, per-sink level filtering, fan-out, `RemoveSink`, `CategoryLogger` entry construction, `IsEnabled` guards, sink failure isolation (no propagation, no fan-out break), `Dispose` of `IDisposable` sinks, convenience method level/exception wiring |
| `FileSinkTest` | 5 | Per-launch file naming, JSON context serialization, exception string output, exception newline escaping to single line, concurrent write integrity (100 parallel writes) |
| `DebugSinkTest` | 4 | Format output with all fields, context via `ToString()`, exception string, combined context + exception |

### Manual testing required

1. Launch the app — verify `hypersonic-yyyyMMdd-HHmmss.log` is created in the expected log directory
2. Trigger a media failure — verify the structured error appears in both debug output and the log file
3. Force an unhandled exception — verify it is captured in the log file with context and stack trace on a single line
4. Verify log file is flushed and released on window close
