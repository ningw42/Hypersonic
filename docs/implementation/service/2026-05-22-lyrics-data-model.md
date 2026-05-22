# Lyrics Data Model (Phase 0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the platform-independent lyric data types in `Hypersonic.Core` that Phase 1 providers and Phase 2 renderer both bind to — types only, no behavior beyond validation invariants.

**Architecture:** Five small types in `Hypersonic.Core/Models/Lyrics/` (one enum, two data classes, one container, one validator). Mirrors BetterLyrics' `BaseLyrics`/`LyricsLine`/`LyricsData` shape but trimmed to the single-lane subset Hypersonic ships; multi-lane (translation/romanization) deferred. Pure data — no parsing, no fetching, no WinUI dependencies, fully unit-tested under `Hypersonic.Core.Tests`.

**Tech Stack:** C# / .NET 10 / `net10.0` class library, MSTest, `System.Text.Json` via `Hypersonic.Core.Helpers.Json`.

---

## Design rationale

### Why these types now

LYRICS.md fixes the type shape *before* providers land in Phase 1 — every provider must adapt its native format (`StructuredLyrics`, LRC, TTML, YRC) into these types, and the Phase 2 renderer reads them directly. Getting the shape wrong here costs rework in two later phases.

### Naming: `LyricsDocument`, not `Lyrics`

LYRICS.md calls the container `Lyrics`. Two problems with that name:

1. **Collision** — `Hypersonic.Client.Models.Entities.Lyrics` already exists (the legacy non-structured Subsonic DTO). Phase 1's `SubsonicLyricsProvider` will reference both types in the same file; one of them needs disambiguation. Renaming the *new* core type is cheaper than renaming the existing client DTO (which has serialization attributes wired to the wire format).
2. **Clarity** — `LyricsDocument` reads as "the complete lyrics artifact for one track," which is what it is. `Lyrics` is ambiguous between "the container" and "the genre concept."

We override LYRICS.md on naming only; everything else (fields, sync-kind enum values, mirror of BetterLyrics shape) is unchanged.

### Sync kind

`LyricsSyncKind { Word, Line, Plain, None }`. Matches LYRICS.md. Semantics:
- `Word` — every line has a non-empty `Words` list. Karaoke-grade.
- `Line` — lines have `StartMs`, words list is empty. Spotify-grade.
- `Plain` — lines have text only, all `StartMs` are zero. Unsynced lyrics body.
- `None` — empty document (used when a provider returns "definitively no lyrics" but we still want to cache the negative result).

Validator enforces these invariants per-document so the renderer can trust the field.

### Mutability and serialization

- All types are `class` (not `record`) with `init`-only setters, matching the existing Core models (`AlbumInfo`, `PlaybackTrack`).
- Collections exposed as `IReadOnlyList<T>` on the API surface, backed by `List<T>` for `System.Text.Json` round-tripping. The constructor accepts `IEnumerable<T>` and snapshots into the backing list — so once constructed, the document is effectively immutable from the renderer's perspective.
- `JsonStringEnumConverter` is already configured globally via `Hypersonic.Core.Helpers.Json`, so `LyricsSyncKind` round-trips as `"Word"` / `"Line"` etc. for legible Phase 1 cache files.

### Validation: separate class, returns issues

`LyricsValidator.Validate(LyricsDocument)` returns `IReadOnlyList<string>` (empty = valid). Does **not** throw. Reasons:
- Providers will see malformed real-world data (LRC files with non-monotonic timestamps, TTML with word ranges that overflow their line). Throwing in a constructor turns one bad line into a totally dropped lyric. Returning issues lets the provider log + decide.
- Constructors stay simple — no validation side-effects, fits the existing Core model style.

Invariants checked:
1. `LyricsLine.StartMs >= 0`
2. `LyricsLine.EndMs`, if set, is `>= StartMs`
3. Lines are monotonic by `StartMs` (allow equality — chorus lines can share a timestamp in some formats, but never go backwards)
4. `LyricsWord.StartMs <= EndMs`
5. Words within a line are monotonic by `StartMs`
6. When `LyricsLine.EndMs` is set, every word's `[StartMs, EndMs]` falls within `[Line.StartMs, Line.EndMs]`
7. `SyncKind == Word` ⟹ every line has at least one word
8. `SyncKind == Line` ⟹ every line has zero words
9. `SyncKind == Plain` ⟹ every line has zero words **and** `StartMs == 0`
10. `SyncKind == None` ⟹ zero lines

### Word-sync source mapping (verified for Phase 1 providers)

Confirmed both deferred word-sync formats map cleanly into `LyricsLine` + `LyricsWord`:

- **AMLL TTML** — `<span begin="..." end="...">word</span>` inside `<p begin="..." end="...">`. `<p>` → `LyricsLine`, each `<span>` → `LyricsWord`. Multi-lane (translation, romanization) lives in sibling `<span ttm:role="x-translation">` — Phase 0 drops these, Phase 2+ can extend.
- **NetEase YRC** — line format `[startMs,durationMs]` then `(wordStartMs,durationMs,0)wordText` repeats. `wordStartMs` is absolute, not relative to the line. Straight mapping; durations convert to `EndMs` by addition.

No model changes needed in Phase 0 to accommodate them later.

### File structure

```
Hypersonic.Core/
└── Models/
    └── Lyrics/                    # new directory
        ├── LyricsSyncKind.cs      # enum
        ├── LyricsWord.cs          # class
        ├── LyricsLine.cs          # class
        ├── LyricsDocument.cs      # container class
        └── LyricsValidator.cs     # static validator
Hypersonic.Core.Tests/
└── Models/
    └── Lyrics/                    # new directory
        ├── LyricsSyncKindTests.cs
        ├── LyricsWordTests.cs
        ├── LyricsLineTests.cs
        ├── LyricsDocumentTests.cs
        └── LyricsValidatorTests.cs
```

One responsibility per file. All under one parent directory so Phase 1's `LyricsService` and Phase 2's `LyricsCanvasControl` can `using Hypersonic.Core.Models.Lyrics;` and pull everything.

---

## Tasks

### Task 1: `LyricsSyncKind` enum

**Files:**
- Create: `Hypersonic.Core/Models/Lyrics/LyricsSyncKind.cs`
- Test: `Hypersonic.Core.Tests/Models/Lyrics/LyricsSyncKindTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Hypersonic.Core.Tests/Models/Lyrics/LyricsSyncKindTests.cs
using System.Text.Json;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsSyncKindTests
{
    [TestMethod]
    public void AllFourMembers_ArePresent()
    {
        var values = Enum.GetValues<LyricsSyncKind>();
        CollectionAssert.AreEquivalent(
            new[] { LyricsSyncKind.Word, LyricsSyncKind.Line, LyricsSyncKind.Plain, LyricsSyncKind.None },
            values);
    }

    [TestMethod]
    public void RoundTripsAsStringViaGlobalJsonOptions()
    {
        var json = JsonSerializer.Serialize(LyricsSyncKind.Word, Json.Options);
        Assert.AreEqual("\"Word\"", json);

        var back = JsonSerializer.Deserialize<LyricsSyncKind>(json, Json.Options);
        Assert.AreEqual(LyricsSyncKind.Word, back);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsSyncKindTests"`
Expected: FAIL — `LyricsSyncKind` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
// Hypersonic.Core/Models/Lyrics/LyricsSyncKind.cs
namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// Granularity of the timing information in a <see cref="LyricsDocument"/>.
/// </summary>
public enum LyricsSyncKind
{
    /// <summary>Every line carries word-level timing. Karaoke-grade.</summary>
    Word,
    /// <summary>Lines carry start timestamps; no word-level timing.</summary>
    Line,
    /// <summary>Plain text lyrics with no timing information.</summary>
    Plain,
    /// <summary>Empty document — definitively no lyrics available for the track.</summary>
    None,
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsSyncKindTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic.Core/Models/Lyrics/LyricsSyncKind.cs Hypersonic.Core.Tests/Models/Lyrics/LyricsSyncKindTests.cs
git commit -m "feat(core): add LyricsSyncKind enum"
```

---

### Task 2: `LyricsWord` type

**Files:**
- Create: `Hypersonic.Core/Models/Lyrics/LyricsWord.cs`
- Test: `Hypersonic.Core.Tests/Models/Lyrics/LyricsWordTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Hypersonic.Core.Tests/Models/Lyrics/LyricsWordTests.cs
using System.Text.Json;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsWordTests
{
    [TestMethod]
    public void Construct_StoresAllFields()
    {
        var w = new LyricsWord { StartMs = 1000, EndMs = 1250, Text = "hello" };

        Assert.AreEqual(1000, w.StartMs);
        Assert.AreEqual(1250, w.EndMs);
        Assert.AreEqual("hello", w.Text);
    }

    [TestMethod]
    public void DurationMs_IsEndMinusStart()
    {
        var w = new LyricsWord { StartMs = 200, EndMs = 750, Text = "x" };
        Assert.AreEqual(550, w.DurationMs);
    }

    [TestMethod]
    public void DurationMs_ClampsToZeroWhenEndBeforeStart()
    {
        // Defensive: validator catches this, but the property shouldn't go negative.
        var w = new LyricsWord { StartMs = 500, EndMs = 200, Text = "x" };
        Assert.AreEqual(0, w.DurationMs);
    }

    [TestMethod]
    public void TextDefaultsToEmpty()
    {
        var w = new LyricsWord();
        Assert.AreEqual(string.Empty, w.Text);
    }

    [TestMethod]
    public void RoundTripsThroughJson()
    {
        var w = new LyricsWord { StartMs = 1000, EndMs = 1500, Text = "hi" };
        var json = JsonSerializer.Serialize(w, Json.Options);
        var back = JsonSerializer.Deserialize<LyricsWord>(json, Json.Options);

        Assert.IsNotNull(back);
        Assert.AreEqual(1000, back!.StartMs);
        Assert.AreEqual(1500, back.EndMs);
        Assert.AreEqual("hi", back.Text);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsWordTests"`
Expected: FAIL — `LyricsWord` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// Hypersonic.Core/Models/Lyrics/LyricsWord.cs
namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// One timed word (or syllable) inside a <see cref="LyricsLine"/>.
/// </summary>
public class LyricsWord
{
    public int StartMs { get; init; }
    public int EndMs { get; init; }
    public string Text { get; init; } = string.Empty;

    public int DurationMs => Math.Max(EndMs - StartMs, 0);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsWordTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic.Core/Models/Lyrics/LyricsWord.cs Hypersonic.Core.Tests/Models/Lyrics/LyricsWordTests.cs
git commit -m "feat(core): add LyricsWord type"
```

---

### Task 3: `LyricsLine` type

**Files:**
- Create: `Hypersonic.Core/Models/Lyrics/LyricsLine.cs`
- Test: `Hypersonic.Core.Tests/Models/Lyrics/LyricsLineTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Hypersonic.Core.Tests/Models/Lyrics/LyricsLineTests.cs
using System.Text.Json;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsLineTests
{
    [TestMethod]
    public void Construct_StoresAllFields()
    {
        var line = new LyricsLine
        {
            StartMs = 5000,
            EndMs = 7500,
            Text = "Hello world",
            Words = new[]
            {
                new LyricsWord { StartMs = 5000, EndMs = 5500, Text = "Hello " },
                new LyricsWord { StartMs = 5500, EndMs = 7500, Text = "world" },
            },
        };

        Assert.AreEqual(5000, line.StartMs);
        Assert.AreEqual(7500, line.EndMs);
        Assert.AreEqual("Hello world", line.Text);
        Assert.AreEqual(2, line.Words.Count);
        Assert.AreEqual("Hello ", line.Words[0].Text);
    }

    [TestMethod]
    public void DefaultsAreSafe()
    {
        var line = new LyricsLine();

        Assert.AreEqual(0, line.StartMs);
        Assert.IsNull(line.EndMs);
        Assert.AreEqual(string.Empty, line.Text);
        Assert.AreEqual(0, line.Words.Count);
    }

    [TestMethod]
    public void Words_ExposedAsReadOnlyList()
    {
        var line = new LyricsLine
        {
            Words = new[] { new LyricsWord { StartMs = 0, EndMs = 100, Text = "x" } },
        };

        // Compile-time check: Words is IReadOnlyList<LyricsWord>, not List<LyricsWord>.
        IReadOnlyList<LyricsWord> ro = line.Words;
        Assert.AreEqual(1, ro.Count);
    }

    [TestMethod]
    public void Words_SnapshotsTheInputEnumerable()
    {
        var mutable = new List<LyricsWord>
        {
            new() { StartMs = 0, EndMs = 100, Text = "a" },
        };
        var line = new LyricsLine { Words = mutable };

        mutable.Add(new LyricsWord { StartMs = 100, EndMs = 200, Text = "b" });

        Assert.AreEqual(1, line.Words.Count, "Mutating the source list must not affect the line.");
    }

    [TestMethod]
    public void RoundTripsThroughJson()
    {
        var line = new LyricsLine
        {
            StartMs = 1000,
            EndMs = 2000,
            Text = "hi",
            Words = new[] { new LyricsWord { StartMs = 1000, EndMs = 2000, Text = "hi" } },
        };

        var json = JsonSerializer.Serialize(line, Json.Options);
        var back = JsonSerializer.Deserialize<LyricsLine>(json, Json.Options);

        Assert.IsNotNull(back);
        Assert.AreEqual(1000, back!.StartMs);
        Assert.AreEqual(2000, back.EndMs);
        Assert.AreEqual("hi", back.Text);
        Assert.AreEqual(1, back.Words.Count);
        Assert.AreEqual("hi", back.Words[0].Text);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsLineTests"`
Expected: FAIL — `LyricsLine` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// Hypersonic.Core/Models/Lyrics/LyricsLine.cs
using System.Text.Json.Serialization;

namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// One timed line of lyrics. May carry word-level timing in <see cref="Words"/>;
/// an empty list means line-only sync.
/// </summary>
public class LyricsLine
{
    private readonly IReadOnlyList<LyricsWord> _words = Array.Empty<LyricsWord>();

    public int StartMs { get; init; }
    public int? EndMs { get; init; }
    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<LyricsWord> Words
    {
        get => _words;
        init => _words = value is null
            ? Array.Empty<LyricsWord>()
            : value.ToList().AsReadOnly();
    }

    [JsonIgnore]
    public int DurationMs => EndMs.HasValue ? Math.Max(EndMs.Value - StartMs, 0) : 0;
}
```

> Note: `Words` snapshots into a `List<>.AsReadOnly()` so subsequent mutation of the source collection can't corrupt the document. `System.Text.Json` deserializes `IReadOnlyList<T>` via the `init` setter — verified by the round-trip test.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsLineTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic.Core/Models/Lyrics/LyricsLine.cs Hypersonic.Core.Tests/Models/Lyrics/LyricsLineTests.cs
git commit -m "feat(core): add LyricsLine type with optional word timings"
```

---

### Task 4: `LyricsDocument` container

**Files:**
- Create: `Hypersonic.Core/Models/Lyrics/LyricsDocument.cs`
- Test: `Hypersonic.Core.Tests/Models/Lyrics/LyricsDocumentTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Hypersonic.Core.Tests/Models/Lyrics/LyricsDocumentTests.cs
using System.Text.Json;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsDocumentTests
{
    [TestMethod]
    public void Construct_StoresAllFields()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            LanguageCode = "en",
            SourceProvider = "subsonic",
            OffsetMs = -200,
            Lines = new[]
            {
                new LyricsLine { StartMs = 0, Text = "first" },
                new LyricsLine { StartMs = 1000, Text = "second" },
            },
        };

        Assert.AreEqual(LyricsSyncKind.Line, doc.SyncKind);
        Assert.AreEqual("en", doc.LanguageCode);
        Assert.AreEqual("subsonic", doc.SourceProvider);
        Assert.AreEqual(-200, doc.OffsetMs);
        Assert.AreEqual(2, doc.Lines.Count);
    }

    [TestMethod]
    public void DefaultsAreSafe()
    {
        var doc = new LyricsDocument();

        Assert.AreEqual(LyricsSyncKind.None, doc.SyncKind);
        Assert.IsNull(doc.LanguageCode);
        Assert.IsNull(doc.SourceProvider);
        Assert.IsNull(doc.OffsetMs);
        Assert.AreEqual(0, doc.Lines.Count);
    }

    [TestMethod]
    public void Lines_ExposedAsReadOnlyList()
    {
        var doc = new LyricsDocument
        {
            Lines = new[] { new LyricsLine { StartMs = 0, Text = "x" } },
        };

        IReadOnlyList<LyricsLine> ro = doc.Lines;
        Assert.AreEqual(1, ro.Count);
    }

    [TestMethod]
    public void Lines_SnapshotsTheInputEnumerable()
    {
        var mutable = new List<LyricsLine>
        {
            new() { StartMs = 0, Text = "a" },
        };
        var doc = new LyricsDocument { Lines = mutable };

        mutable.Add(new LyricsLine { StartMs = 1000, Text = "b" });

        Assert.AreEqual(1, doc.Lines.Count);
    }

    [TestMethod]
    public void Empty_IsValidNoneDocument()
    {
        var doc = LyricsDocument.Empty(sourceProvider: "lrclib");

        Assert.AreEqual(LyricsSyncKind.None, doc.SyncKind);
        Assert.AreEqual("lrclib", doc.SourceProvider);
        Assert.AreEqual(0, doc.Lines.Count);
    }

    [TestMethod]
    public void RoundTripsThroughJson_WithSyncKindAsString()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            LanguageCode = "ja",
            SourceProvider = "amll-ttml-db",
            OffsetMs = 0,
            Lines = new[]
            {
                new LyricsLine
                {
                    StartMs = 1000,
                    EndMs = 2000,
                    Text = "hi",
                    Words = new[] { new LyricsWord { StartMs = 1000, EndMs = 2000, Text = "hi" } },
                },
            },
        };

        var json = JsonSerializer.Serialize(doc, Json.Options);
        StringAssert.Contains(json, "\"Word\"", "SyncKind must serialize as a string for readable cache files.");

        var back = JsonSerializer.Deserialize<LyricsDocument>(json, Json.Options);
        Assert.IsNotNull(back);
        Assert.AreEqual(LyricsSyncKind.Word, back!.SyncKind);
        Assert.AreEqual("ja", back.LanguageCode);
        Assert.AreEqual("amll-ttml-db", back.SourceProvider);
        Assert.AreEqual(1, back.Lines.Count);
        Assert.AreEqual(1, back.Lines[0].Words.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsDocumentTests"`
Expected: FAIL — `LyricsDocument` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// Hypersonic.Core/Models/Lyrics/LyricsDocument.cs
namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// Complete lyrics artifact for one track. Container for an ordered list of
/// <see cref="LyricsLine"/> plus metadata (sync granularity, language, source,
/// global offset).
/// </summary>
/// <remarks>
/// Named <c>LyricsDocument</c> rather than <c>Lyrics</c> to avoid colliding
/// with <c>Hypersonic.Client.Models.Entities.Lyrics</c> (the legacy Subsonic
/// DTO). See the Phase 0 design doc for rationale.
/// </remarks>
public class LyricsDocument
{
    private readonly IReadOnlyList<LyricsLine> _lines = Array.Empty<LyricsLine>();

    public LyricsSyncKind SyncKind { get; init; } = LyricsSyncKind.None;

    /// <summary>BCP-47 language code, when known (e.g. "en", "ja").</summary>
    public string? LanguageCode { get; init; }

    /// <summary>Identifier of the provider that produced this document (e.g. "subsonic", "lrclib").</summary>
    public string? SourceProvider { get; init; }

    /// <summary>Global offset in ms applied to every timestamp at render time. Negative = lyrics earlier than audio.</summary>
    public int? OffsetMs { get; init; }

    public IReadOnlyList<LyricsLine> Lines
    {
        get => _lines;
        init => _lines = value is null
            ? Array.Empty<LyricsLine>()
            : value.ToList().AsReadOnly();
    }

    /// <summary>Factory for the "definitively no lyrics" result, suitable for negative caching.</summary>
    public static LyricsDocument Empty(string? sourceProvider = null) => new()
    {
        SyncKind = LyricsSyncKind.None,
        SourceProvider = sourceProvider,
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsDocumentTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic.Core/Models/Lyrics/LyricsDocument.cs Hypersonic.Core.Tests/Models/Lyrics/LyricsDocumentTests.cs
git commit -m "feat(core): add LyricsDocument container"
```

---

### Task 5: `LyricsValidator` — invariants

**Files:**
- Create: `Hypersonic.Core/Models/Lyrics/LyricsValidator.cs`
- Test: `Hypersonic.Core.Tests/Models/Lyrics/LyricsValidatorTests.cs`

This task lands one invariant at a time. Each invariant gets a test → run-red → minimal-code → run-green → commit cycle. After all invariants are in, one final pass tests a valid happy-path document for each `SyncKind`.

- [ ] **Step 1: Write the failing test for line StartMs non-negative**

```csharp
// Hypersonic.Core.Tests/Models/Lyrics/LyricsValidatorTests.cs
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsValidatorTests
{
    [TestMethod]
    public void Line_NegativeStartMs_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Lines = new[] { new LyricsLine { StartMs = -1, Text = "x" } },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "StartMs");
        StringAssert.Contains(issues[0], "negative");
    }
}
```

- [ ] **Step 2: Run it red**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsValidatorTests"`
Expected: FAIL — `LyricsValidator` does not exist.

- [ ] **Step 3: Write minimal implementation for the first invariant**

```csharp
// Hypersonic.Core/Models/Lyrics/LyricsValidator.cs
namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// Pure-function validator for <see cref="LyricsDocument"/>. Returns the list
/// of invariants the document violates; an empty list means the document is
/// safe for the renderer to consume. Never throws.
/// </summary>
public static class LyricsValidator
{
    public static IReadOnlyList<string> Validate(LyricsDocument document)
    {
        var issues = new List<string>();

        for (var i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];
            if (line.StartMs < 0)
            {
                issues.Add($"Line {i}: StartMs is negative ({line.StartMs}).");
            }
        }

        return issues;
    }
}
```

- [ ] **Step 4: Run it green**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsValidatorTests"`
Expected: PASS (1 test).

- [ ] **Step 5: Add tests for invariants 2 through 6 (timing structure)**

Append to `LyricsValidatorTests.cs`:

```csharp
[TestMethod]
public void Line_EndBeforeStart_IsReportedAsIssue()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Line,
        Lines = new[] { new LyricsLine { StartMs = 1000, EndMs = 500, Text = "x" } },
    };

    var issues = LyricsValidator.Validate(doc);

    Assert.AreEqual(1, issues.Count);
    StringAssert.Contains(issues[0], "EndMs");
}

[TestMethod]
public void Lines_OutOfOrder_IsReportedAsIssue()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Line,
        Lines = new[]
        {
            new LyricsLine { StartMs = 2000, Text = "second" },
            new LyricsLine { StartMs = 1000, Text = "first" },
        },
    };

    var issues = LyricsValidator.Validate(doc);

    Assert.IsTrue(issues.Any(i => i.Contains("monotonic", StringComparison.OrdinalIgnoreCase)),
        "Expected a monotonicity issue, got: " + string.Join("; ", issues));
}

[TestMethod]
public void Lines_EqualStartMs_IsAllowed()
{
    // Some formats let chorus lines share a timestamp — allow equality, only reject going backwards.
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Line,
        Lines = new[]
        {
            new LyricsLine { StartMs = 1000, Text = "a" },
            new LyricsLine { StartMs = 1000, Text = "b" },
        },
    };

    Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
}

[TestMethod]
public void Word_EndBeforeStart_IsReportedAsIssue()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Word,
        Lines = new[]
        {
            new LyricsLine
            {
                StartMs = 1000,
                EndMs = 2000,
                Text = "x",
                Words = new[] { new LyricsWord { StartMs = 1500, EndMs = 1200, Text = "x" } },
            },
        },
    };

    var issues = LyricsValidator.Validate(doc);

    Assert.IsTrue(issues.Any(i => i.Contains("Word", StringComparison.Ordinal)
                                  && i.Contains("EndMs", StringComparison.Ordinal)));
}

[TestMethod]
public void Words_OutOfOrder_IsReportedAsIssue()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Word,
        Lines = new[]
        {
            new LyricsLine
            {
                StartMs = 1000,
                EndMs = 3000,
                Text = "a b",
                Words = new[]
                {
                    new LyricsWord { StartMs = 2000, EndMs = 2500, Text = "b" },
                    new LyricsWord { StartMs = 1000, EndMs = 1500, Text = "a" },
                },
            },
        },
    };

    var issues = LyricsValidator.Validate(doc);

    Assert.IsTrue(issues.Any(i => i.Contains("Word", StringComparison.Ordinal)
                                  && i.Contains("monotonic", StringComparison.OrdinalIgnoreCase)));
}

[TestMethod]
public void Word_OutsideLineBounds_IsReportedAsIssue()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Word,
        Lines = new[]
        {
            new LyricsLine
            {
                StartMs = 1000,
                EndMs = 2000,
                Text = "x",
                Words = new[] { new LyricsWord { StartMs = 1500, EndMs = 2500, Text = "x" } },
            },
        },
    };

    var issues = LyricsValidator.Validate(doc);

    Assert.IsTrue(issues.Any(i => i.Contains("outside line bounds", StringComparison.OrdinalIgnoreCase)));
}

[TestMethod]
public void Word_OutsideLineBounds_NotCheckedWhenLineEndMsIsNull()
{
    // No upper bound on the line → can't check words against it.
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Word,
        Lines = new[]
        {
            new LyricsLine
            {
                StartMs = 1000,
                EndMs = null,
                Text = "x",
                Words = new[] { new LyricsWord { StartMs = 1500, EndMs = 9999, Text = "x" } },
            },
        },
    };

    Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
}
```

- [ ] **Step 6: Run new tests red, then extend the validator**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsValidatorTests"`
Expected: FAIL — most of the new tests fail.

Replace `LyricsValidator.cs` with the timing-structure version:

```csharp
// Hypersonic.Core/Models/Lyrics/LyricsValidator.cs
namespace Hypersonic.Core.Models.Lyrics;

public static class LyricsValidator
{
    public static IReadOnlyList<string> Validate(LyricsDocument document)
    {
        var issues = new List<string>();
        int? previousLineStart = null;

        for (var i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];

            if (line.StartMs < 0)
            {
                issues.Add($"Line {i}: StartMs is negative ({line.StartMs}).");
            }

            if (line.EndMs.HasValue && line.EndMs.Value < line.StartMs)
            {
                issues.Add($"Line {i}: EndMs ({line.EndMs.Value}) is before StartMs ({line.StartMs}).");
            }

            if (previousLineStart.HasValue && line.StartMs < previousLineStart.Value)
            {
                issues.Add($"Line {i}: lines are not monotonic by StartMs ({line.StartMs} < {previousLineStart.Value}).");
            }
            previousLineStart = line.StartMs;

            int? previousWordStart = null;
            for (var w = 0; w < line.Words.Count; w++)
            {
                var word = line.Words[w];

                if (word.EndMs < word.StartMs)
                {
                    issues.Add($"Line {i}, Word {w}: EndMs ({word.EndMs}) is before StartMs ({word.StartMs}).");
                }

                if (previousWordStart.HasValue && word.StartMs < previousWordStart.Value)
                {
                    issues.Add($"Line {i}, Word {w}: words are not monotonic by StartMs.");
                }
                previousWordStart = word.StartMs;

                if (line.EndMs.HasValue
                    && (word.StartMs < line.StartMs || word.EndMs > line.EndMs.Value))
                {
                    issues.Add($"Line {i}, Word {w}: timing [{word.StartMs}, {word.EndMs}] falls outside line bounds [{line.StartMs}, {line.EndMs.Value}].");
                }
            }
        }

        return issues;
    }
}
```

- [ ] **Step 7: Run new tests green**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsValidatorTests"`
Expected: PASS (8 tests).

- [ ] **Step 8: Add tests for invariants 7 through 10 (SyncKind consistency)**

Append:

```csharp
[TestMethod]
public void SyncKindWord_LineWithoutWords_IsReportedAsIssue()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Word,
        Lines = new[] { new LyricsLine { StartMs = 0, Text = "x" } },
    };

    var issues = LyricsValidator.Validate(doc);

    Assert.IsTrue(issues.Any(i => i.Contains("Word", StringComparison.Ordinal)
                                  && i.Contains("requires", StringComparison.OrdinalIgnoreCase)));
}

[TestMethod]
public void SyncKindLine_LineWithWords_IsReportedAsIssue()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Line,
        Lines = new[]
        {
            new LyricsLine
            {
                StartMs = 0,
                EndMs = 1000,
                Text = "x",
                Words = new[] { new LyricsWord { StartMs = 0, EndMs = 1000, Text = "x" } },
            },
        },
    };

    var issues = LyricsValidator.Validate(doc);

    Assert.IsTrue(issues.Any(i => i.Contains("Line", StringComparison.Ordinal)
                                  && i.Contains("must not", StringComparison.OrdinalIgnoreCase)));
}

[TestMethod]
public void SyncKindPlain_LineWithStartMs_IsReportedAsIssue()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Plain,
        Lines = new[] { new LyricsLine { StartMs = 1000, Text = "x" } },
    };

    var issues = LyricsValidator.Validate(doc);

    Assert.IsTrue(issues.Any(i => i.Contains("Plain", StringComparison.Ordinal)));
}

[TestMethod]
public void SyncKindPlain_LineWithWords_IsReportedAsIssue()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Plain,
        Lines = new[]
        {
            new LyricsLine
            {
                StartMs = 0,
                Text = "x",
                Words = new[] { new LyricsWord { StartMs = 0, EndMs = 0, Text = "x" } },
            },
        },
    };

    var issues = LyricsValidator.Validate(doc);

    Assert.IsTrue(issues.Any(i => i.Contains("Plain", StringComparison.Ordinal)));
}

[TestMethod]
public void SyncKindNone_AnyLine_IsReportedAsIssue()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.None,
        Lines = new[] { new LyricsLine { StartMs = 0, Text = "x" } },
    };

    var issues = LyricsValidator.Validate(doc);

    Assert.IsTrue(issues.Any(i => i.Contains("None", StringComparison.Ordinal)));
}

[TestMethod]
public void HappyPath_WordSyncedDocument_HasNoIssues()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Word,
        LanguageCode = "en",
        SourceProvider = "amll-ttml-db",
        Lines = new[]
        {
            new LyricsLine
            {
                StartMs = 1000, EndMs = 3000, Text = "hello world",
                Words = new[]
                {
                    new LyricsWord { StartMs = 1000, EndMs = 1500, Text = "hello " },
                    new LyricsWord { StartMs = 1500, EndMs = 3000, Text = "world" },
                },
            },
        },
    };

    Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
}

[TestMethod]
public void HappyPath_LineSyncedDocument_HasNoIssues()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Line,
        Lines = new[]
        {
            new LyricsLine { StartMs = 0, Text = "first" },
            new LyricsLine { StartMs = 2000, Text = "second" },
        },
    };

    Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
}

[TestMethod]
public void HappyPath_PlainDocument_HasNoIssues()
{
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Plain,
        Lines = new[]
        {
            new LyricsLine { Text = "line one" },
            new LyricsLine { Text = "line two" },
        },
    };

    Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
}

[TestMethod]
public void HappyPath_NoneDocument_HasNoIssues()
{
    Assert.AreEqual(0, LyricsValidator.Validate(LyricsDocument.Empty()).Count);
}
```

- [ ] **Step 9: Run new tests red, then extend the validator**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsValidatorTests"`
Expected: FAIL — new SyncKind-consistency tests fail.

Add the SyncKind-consistency block to `LyricsValidator.cs`. Replace the file with:

```csharp
// Hypersonic.Core/Models/Lyrics/LyricsValidator.cs
namespace Hypersonic.Core.Models.Lyrics;

public static class LyricsValidator
{
    public static IReadOnlyList<string> Validate(LyricsDocument document)
    {
        var issues = new List<string>();
        int? previousLineStart = null;

        for (var i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];
            ValidateLineTimings(issues, i, line, ref previousLineStart);
            ValidateWordsWithinLine(issues, i, line);
            ValidateSyncKindForLine(issues, i, line, document.SyncKind);
        }

        if (document.SyncKind == LyricsSyncKind.None && document.Lines.Count > 0)
        {
            issues.Add($"SyncKind=None requires zero lines, but document has {document.Lines.Count}.");
        }

        return issues;
    }

    private static void ValidateLineTimings(List<string> issues, int i, LyricsLine line, ref int? previousLineStart)
    {
        if (line.StartMs < 0)
        {
            issues.Add($"Line {i}: StartMs is negative ({line.StartMs}).");
        }

        if (line.EndMs.HasValue && line.EndMs.Value < line.StartMs)
        {
            issues.Add($"Line {i}: EndMs ({line.EndMs.Value}) is before StartMs ({line.StartMs}).");
        }

        if (previousLineStart.HasValue && line.StartMs < previousLineStart.Value)
        {
            issues.Add($"Line {i}: lines are not monotonic by StartMs ({line.StartMs} < {previousLineStart.Value}).");
        }
        previousLineStart = line.StartMs;
    }

    private static void ValidateWordsWithinLine(List<string> issues, int i, LyricsLine line)
    {
        int? previousWordStart = null;
        for (var w = 0; w < line.Words.Count; w++)
        {
            var word = line.Words[w];

            if (word.EndMs < word.StartMs)
            {
                issues.Add($"Line {i}, Word {w}: EndMs ({word.EndMs}) is before StartMs ({word.StartMs}).");
            }

            if (previousWordStart.HasValue && word.StartMs < previousWordStart.Value)
            {
                issues.Add($"Line {i}, Word {w}: words are not monotonic by StartMs.");
            }
            previousWordStart = word.StartMs;

            if (line.EndMs.HasValue
                && (word.StartMs < line.StartMs || word.EndMs > line.EndMs.Value))
            {
                issues.Add($"Line {i}, Word {w}: timing [{word.StartMs}, {word.EndMs}] falls outside line bounds [{line.StartMs}, {line.EndMs.Value}].");
            }
        }
    }

    private static void ValidateSyncKindForLine(List<string> issues, int i, LyricsLine line, LyricsSyncKind kind)
    {
        switch (kind)
        {
            case LyricsSyncKind.Word:
                if (line.Words.Count == 0)
                {
                    issues.Add($"Line {i}: SyncKind=Word requires at least one word.");
                }
                break;

            case LyricsSyncKind.Line:
                if (line.Words.Count > 0)
                {
                    issues.Add($"Line {i}: SyncKind=Line must not carry word-level timings.");
                }
                break;

            case LyricsSyncKind.Plain:
                if (line.StartMs != 0)
                {
                    issues.Add($"Line {i}: SyncKind=Plain requires StartMs=0 (got {line.StartMs}).");
                }
                if (line.Words.Count > 0)
                {
                    issues.Add($"Line {i}: SyncKind=Plain must not carry word-level timings.");
                }
                break;

            // None is handled outside the per-line loop.
        }
    }
}
```

- [ ] **Step 10: Run all validator tests green**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsValidatorTests"`
Expected: PASS (17 tests).

- [ ] **Step 11: Commit**

```bash
git add Hypersonic.Core/Models/Lyrics/LyricsValidator.cs Hypersonic.Core.Tests/Models/Lyrics/LyricsValidatorTests.cs
git commit -m "feat(core): add LyricsValidator with timing and SyncKind invariants"
```

---

### Task 6: Final sweep — full solution build and full test run

- [ ] **Step 1: Build the solution**

Run: `dotnet build Hypersonic.sln -p:Platform=x64`
Expected: Build succeeds with no warnings related to the new files. (Pre-existing warnings unchanged.)

- [ ] **Step 2: Run the full Core test suite**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`
Expected: PASS. Total includes the 5 new test classes plus all pre-existing tests; the new classes contribute the test counts from Tasks 1–5.

- [ ] **Step 3: Update LYRICS.md**

Tick Phase 0 in `LYRICS.md` (replace the Phase 0 section heading line with one indicating completion, or — if no convention yet exists — add a checkbox at the start of the section: `### Phase 0 — Data model (`Hypersonic.Core`) — ✅ shipped`). Add a one-line note pointing back to this implementation doc.

- [ ] **Step 4: Commit the tracking-doc update**

```bash
git add LYRICS.md
git commit -m "docs(lyrics): mark Phase 0 (data model) shipped"
```

---

## Self-review notes

- **Spec coverage check:** every bullet under "Phase 0 — Data model" in `LYRICS.md` is covered — `LyricsLine` (Task 3), `LyricsWord` (Task 2), container with `SyncKind` and `OffsetMs` (Task 4), validation invariants (Task 5), shape mirrors BetterLyrics (line-only lines have empty `Words` — Task 3 default test), no behavior / no parsing (no task adds behavior beyond the validator).
- **Naming deviation:** Container renamed `Lyrics` → `LyricsDocument`. Rationale documented in "Naming" section above. Phase 1 plan must use `LyricsDocument` in `ILyricsProvider` and `LyricsService`.
- **Phase 1 hand-off contract:** `ILyricsProvider.GetAsync(...)` returns `Task<LyricsDocument?>` — `null` means "no answer / unknown" (use the next provider), `LyricsDocument.Empty(...)` means "definitively no lyrics" (cache the negative and stop). Validator should be invoked once per provider response; documents with issues are logged and discarded.
- **AMLL TTML / NetEase YRC mapping:** Confirmed clean. See "Word-sync source mapping" in the design rationale.

---

## Execution Handoff

Plan complete and saved to `docs/implementation/service/2026-05-22-lyrics-data-model.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
