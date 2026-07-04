# Lyrics Format & Provenance Unification + Source Badge — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify the lyric payload-format axis into one typed `LyricsFormat` enum (merging the string `PayloadFormat` tag), make provider identity (`SourceProvider`) and format orthogonal with clear owners, and surface the lyrics source as an info-glyph tooltip on Now Playing.

**Architecture:** Two stages. **Stage 1** is an invisible model refactor: introduce `LyricsFormat`, dedup the two pairs of byte-identical parsers, flip the parser/registry/result/cache-entry/service/provider/client pipeline from string tags to the enum, move `SourceProvider` stamping from parsers to providers (fixing the AMLL mis-attribution), and migrate the SQLite cache. **Stage 2** rides on Stage 1: a `DisplayName` per provider, a tiny catalog service, three derived VM properties, and an info glyph on `NowPlayingPage`.

**Tech Stack:** .NET 10, C#, WinUI 3 (Windows App SDK 2.2.0), CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, MSTest.

Design source of truth: [`docs/design/service/2026-07-04-lyrics-format-provenance-unification.md`](../design/service/2026-07-04-lyrics-format-provenance-unification.md).

## Global Constraints

- **Target framework:** `net10.0` (Core) / `net10.0-windows10.0.19041.0` (app). The main app cannot `dotnet run`.
- **Build:** `dotnet build Hypersonic.sln -p:Platform=x64`. Always pass `-p:Platform=x64` for the app/app-tests.
- **Core tests:** `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj` (plain, no platform flag).
- **App tests:** `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`.
- **Single test:** append `--filter "FullyQualifiedName~TestName"`.
- **Enum storage in SQLite:** store as the enum **name** via `.ToString()`, read via `Enum.TryParse<T>(…, out var v) ? v : <fallback>` — this mirrors the existing `SyncKind` column.
- **Code style (.editorconfig):** 4-space Allman braces, file-scoped namespaces, `var`, PascalCase, `I`-prefixed interfaces, CRLF.
- **Commits:** Conventional Commits with scope, e.g. `refactor(lyrics): …`, `feat(lyrics): …`, `test(lyrics): …`.
- **Provider display names (verbatim):** subsonic→`Subsonic`, lrclib→`LrcLib`, amll-ttml-db→`AMLL`, qq→`QQ Music`, netease→`Netease`, kugou→`Kugou`.

---

## File Structure

**Stage 1 — model unification**
- Create: `Hypersonic.Core/Models/Lyrics/LyricsFormat.cs` — the format enum.
- Modify: `Hypersonic.Core/Models/Lyrics/LyricsDocument.cs` — add `Format` + `WithSourceProvider`.
- Modify: `Hypersonic.Core/Contracts/Lyrics/ILyricsPayloadParser.cs`, `ILyricsParserRegistry.cs` — string→enum.
- Modify: `Hypersonic.Core/Services/Lyrics/LyricsParserRegistry.cs` — key by enum.
- Modify: `Hypersonic.Core/Models/Lyrics/LyricsProviderResult.cs` — `PayloadFormat`→`Format`.
- Modify: `Hypersonic.Core/Services/Lyrics/{LrcSyncedLyricsParser,LrcPlainLyricsParser,TtmlLyricsParser}.cs` — set `Format`, drop `SourceProvider`.
- Delete: `Hypersonic.Core/Services/Lyrics/NeteaseLrcLyricsParser.cs`.
- Modify: `Hypersonic/Services/Lyrics/{SubsonicStructuredLyricsParser,LyricifyWordLyricsParser,LyricifySyllableMapper,ILyricifyLyricsClient,LyricifyLyricsClient}.cs`.
- Delete: `Hypersonic/Services/Lyrics/SubsonicLegacyLyricsParser.cs`.
- Modify: providers `Hypersonic/Services/Lyrics/{SubsonicLyricsProvider,LrcLibLyricsProvider,AmllTtmlLyricsProvider,LyricifyLyricsProviderBase}.cs` — stamp `SourceProvider`, pass enum.
- Modify: `Hypersonic/Services/Lyrics/LocalFileLyricsSource.cs` — `Parse(LyricsFormat.Ttml, …)`.
- Modify: `Hypersonic/Services/LyricsService.cs` — `ExtensionForFormat(LyricsFormat)`, `OverlayRowMetadata` preserves `Format`, write paths use `Format`.
- Modify: `Hypersonic/Models/LyricsCacheEntry.cs`, `Hypersonic/Services/SqliteLyricsCacheStore.cs` — `Format` column + `v2` discard migration.
- Modify: `Hypersonic/App.xaml.cs` — parser registrations.

**Stage 2 — source badge**
- Modify: `Hypersonic.Core/Contracts/Lyrics/ILyricsProvider.cs` — add `DisplayName`.
- Modify: all 6 providers + `LyricifyLyricsProviderBase` — implement `DisplayName`.
- Create: `Hypersonic/Contracts/Services/ILyricsSourceCatalog.cs`, `Hypersonic/Services/LyricsSourceCatalog.cs`.
- Modify: `Hypersonic/App.xaml.cs` — register catalog.
- Modify: `Hypersonic/ViewModels/NowPlayingViewModel.cs` — inject catalog, add derived props.
- Modify: `Hypersonic/Views/NowPlayingPage.xaml` — info glyph.

---

# Stage 1 — Model Unification

### Task 1: Add the `LyricsFormat` enum

**Files:**
- Create: `Hypersonic.Core/Models/Lyrics/LyricsFormat.cs`
- Test: `Hypersonic.Core.Tests/Models/Lyrics/LyricsFormatTests.cs`

**Interfaces:**
- Produces: `enum LyricsFormat { Unknown, PlainText, Lrc, Ttml, Qrc, Krc, Yrc, SubsonicStructured }` in namespace `Hypersonic.Core.Models.Lyrics`. `Unknown` is the zero/default value.

- [ ] **Step 1: Write the failing test**

```csharp
// Hypersonic.Core.Tests/Models/Lyrics/LyricsFormatTests.cs
using Hypersonic.Core.Models.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsFormatTests
{
    [TestMethod]
    public void Default_IsUnknown()
        => Assert.AreEqual(LyricsFormat.Unknown, default(LyricsFormat));

    [TestMethod]
    public void DefinesEveryWireFormat()
    {
        // One member per genuinely-distinct wire format the providers emit.
        CollectionAssert.AreEquivalent(
            new[] { "Unknown", "PlainText", "Lrc", "Ttml", "Qrc", "Krc", "Yrc", "SubsonicStructured" },
            System.Enum.GetNames<LyricsFormat>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsFormatTests"`
Expected: FAIL (build error — `LyricsFormat` does not exist).

- [ ] **Step 3: Create the enum**

```csharp
// Hypersonic.Core/Models/Lyrics/LyricsFormat.cs
namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// The wire/file format of a lyrics payload — one member per genuinely-distinct parsing algorithm.
/// Orthogonal to <see cref="LyricsDocument.SourceProvider"/> (who served it) and
/// <see cref="LyricsSyncKind"/> (granularity). Also the parser-dispatch key in
/// <c>ILyricsParserRegistry</c>. <see cref="Unknown"/> is the default (negative-cache rows,
/// unparseable payloads).
/// </summary>
public enum LyricsFormat
{
    Unknown,
    PlainText,
    Lrc,
    Ttml,
    Qrc,
    Krc,
    Yrc,
    SubsonicStructured,
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsFormatTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic.Core/Models/Lyrics/LyricsFormat.cs Hypersonic.Core.Tests/Models/Lyrics/LyricsFormatTests.cs
git commit -m "feat(lyrics): add LyricsFormat enum"
```

---

### Task 2: Add `Format` + `WithSourceProvider` to `LyricsDocument`

**Files:**
- Modify: `Hypersonic.Core/Models/Lyrics/LyricsDocument.cs`
- Test: `Hypersonic.Core.Tests/Models/Lyrics/LyricsDocumentTests.cs` (append)

**Interfaces:**
- Produces: `LyricsFormat LyricsDocument.Format { get; init; }` (default `Unknown`); `LyricsDocument LyricsDocument.WithSourceProvider(string sourceProvider)` — returns a copy with `SourceProvider` set, all other fields (incl. `Format`, `SyncKind`, `Lines`) preserved.

- [ ] **Step 1: Write the failing tests**

```csharp
// Append to Hypersonic.Core.Tests/Models/Lyrics/LyricsDocumentTests.cs
[TestMethod]
public void Format_DefaultsToUnknown()
    => Assert.AreEqual(LyricsFormat.Unknown, new LyricsDocument().Format);

[TestMethod]
public void WithSourceProvider_SetsProvider_PreservesEverythingElse()
{
    var original = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Line,
        Format = LyricsFormat.Lrc,
        LanguageCode = "en",
        OffsetMs = -200,
        Lines = new[] { new LyricsLine { StartMs = 0, Text = "hi" } },
    };

    var stamped = original.WithSourceProvider("lrclib");

    Assert.AreEqual("lrclib", stamped.SourceProvider);
    Assert.AreEqual(LyricsFormat.Lrc, stamped.Format);
    Assert.AreEqual(LyricsSyncKind.Line, stamped.SyncKind);
    Assert.AreEqual("en", stamped.LanguageCode);
    Assert.AreEqual(-200, stamped.OffsetMs);
    Assert.AreEqual(1, stamped.Lines.Count);
    Assert.IsNull(original.SourceProvider); // original is untouched
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsDocumentTests"`
Expected: FAIL (build error — `Format` / `WithSourceProvider` missing).

- [ ] **Step 3: Add the property and helper**

In `Hypersonic.Core/Models/Lyrics/LyricsDocument.cs`, add after the `SourceProvider` property:

```csharp
    /// <summary>Wire format of the payload this document was parsed from. Orthogonal to
    /// <see cref="SourceProvider"/>; stamped by the parser.</summary>
    public LyricsFormat Format { get; init; } = LyricsFormat.Unknown;
```

And add this method inside the class (e.g. above the `Empty` factory):

```csharp
    /// <summary>Returns a copy with <see cref="SourceProvider"/> set; every other field is preserved.
    /// Used by providers to stamp identity onto a parser-produced document (init-only rebuild).</summary>
    public LyricsDocument WithSourceProvider(string sourceProvider) => new()
    {
        SyncKind = SyncKind,
        Format = Format,
        LanguageCode = LanguageCode,
        OffsetMs = OffsetMs,
        SourceProvider = sourceProvider,
        Lines = Lines,
    };
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsDocumentTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hypersonic.Core/Models/Lyrics/LyricsDocument.cs Hypersonic.Core.Tests/Models/Lyrics/LyricsDocumentTests.cs
git commit -m "feat(lyrics): add LyricsDocument.Format and WithSourceProvider copy helper"
```

---

### Task 3: The unification refactor (atomic)

> **This is one atomic compile unit.** The interface/model signature changes (`ILyricsPayloadParser.Format`, `ILyricsParserRegistry.Parse`, `LyricsProviderResult.Content`, `LyricsCacheEntry.Format`) ripple through every parser, provider, the client, the service, and the cache store simultaneously. **The solution will not build until every step below is done.** Intermediate `dotnet build` runs are expected to fail with "not-yet-updated" errors; only the final build+test step must be green. Work top-to-bottom, then build.

**Files (all Modify unless noted):**
- `Hypersonic.Core/Contracts/Lyrics/ILyricsPayloadParser.cs`, `ILyricsParserRegistry.cs`
- `Hypersonic.Core/Services/Lyrics/LyricsParserRegistry.cs`
- `Hypersonic.Core/Models/Lyrics/LyricsProviderResult.cs`
- `Hypersonic.Core/Services/Lyrics/LrcSyncedLyricsParser.cs`, `LrcPlainLyricsParser.cs`, `TtmlLyricsParser.cs`
- Delete: `Hypersonic.Core/Services/Lyrics/NeteaseLrcLyricsParser.cs`
- `Hypersonic/Services/Lyrics/SubsonicStructuredLyricsParser.cs`, `LyricifyWordLyricsParser.cs`, `LyricifySyllableMapper.cs`, `ILyricifyLyricsClient.cs`, `LyricifyLyricsClient.cs`, `LocalFileLyricsSource.cs`
- Delete: `Hypersonic/Services/Lyrics/SubsonicLegacyLyricsParser.cs`
- `Hypersonic/Services/Lyrics/SubsonicLyricsProvider.cs`, `LrcLibLyricsProvider.cs`, `AmllTtmlLyricsProvider.cs`, `LyricifyLyricsProviderBase.cs`
- `Hypersonic/Services/LyricsService.cs`
- `Hypersonic/Models/LyricsCacheEntry.cs`, `Hypersonic/Services/SqliteLyricsCacheStore.cs`
- `Hypersonic/App.xaml.cs`
- Tests across `Hypersonic.Core.Tests` and `Hypersonic.Tests` (Step 3.13)

**Interfaces:**
- Consumes: `LyricsFormat` (Task 1), `LyricsDocument.Format` + `WithSourceProvider` (Task 2).
- Produces:
  - `LyricsFormat ILyricsPayloadParser.Format { get; }` (replaces `string FormatTag`)
  - `LyricsDocument? ILyricsParserRegistry.Parse(LyricsFormat format, ReadOnlyMemory<byte> payload)`
  - `LyricsProviderResult.Content(LyricsDocument, ReadOnlyMemory<byte>, LyricsFormat, bool)`; `LyricsFormat LyricsProviderResult.Format { get; }` (replaces `string? PayloadFormat`)
  - `LyricsFormat LyricsCacheEntry.Format { get; set; }` (replaces `string PayloadFormat`)
  - `record LyricifyLyricsResult(string RawText, LyricsFormat Format, LyricsCandidate Candidate)` (replaces `FormatTag`)

- [ ] **Step 3.1: `ILyricsPayloadParser` — `FormatTag`→`Format`**

Replace the interface body in `Hypersonic.Core/Contracts/Lyrics/ILyricsPayloadParser.cs`:

```csharp
public interface ILyricsPayloadParser
{
    /// <summary>The format this parser handles — also its dispatch key in the registry.</summary>
    LyricsFormat Format { get; }

    /// <summary>Parses <paramref name="payload"/> into a validated document, or <c>null</c> when unusable.</summary>
    LyricsDocument? Parse(ReadOnlyMemory<byte> payload);
}
```

- [ ] **Step 3.2: `ILyricsParserRegistry` + `LyricsParserRegistry` — key by enum**

`Hypersonic.Core/Contracts/Lyrics/ILyricsParserRegistry.cs`:

```csharp
public interface ILyricsParserRegistry
{
    /// <summary>Parses <paramref name="payload"/> with the parser registered for <paramref name="format"/>.
    /// Returns <c>null</c> when no parser matches or the parser rejects the payload.</summary>
    LyricsDocument? Parse(LyricsFormat format, ReadOnlyMemory<byte> payload);
}
```

`Hypersonic.Core/Services/Lyrics/LyricsParserRegistry.cs` — replace the dictionary type, keying, and `Parse`:

```csharp
    private readonly Dictionary<LyricsFormat, ILyricsPayloadParser> _parsers;
    private readonly ILogger _log;

    public LyricsParserRegistry(IEnumerable<ILyricsPayloadParser> parsers, ILoggingService logService)
    {
        _parsers = new Dictionary<LyricsFormat, ILyricsPayloadParser>();
        foreach (var parser in parsers)
        {
            _parsers[parser.Format] = parser;
        }

        _log = logService.GetLogger(nameof(LyricsParserRegistry));
    }

    public LyricsDocument? Parse(LyricsFormat format, ReadOnlyMemory<byte> payload)
    {
        if (!_parsers.TryGetValue(format, out var parser))
        {
            _log.Warning("No lyrics parser registered for format", new { Format = format });
            return null;
        }

        return parser.Parse(payload);
    }
```

- [ ] **Step 3.3: `LyricsProviderResult` — `PayloadFormat`(string)→`Format`(enum)**

In `Hypersonic.Core/Models/Lyrics/LyricsProviderResult.cs`: change the ctor param `string? payloadFormat`→`LyricsFormat format`, the field/property `PayloadFormat`→`Format`, and the factories. Result:

```csharp
    private LyricsProviderResult(
        LyricsOutcomeKind kind,
        LyricsDocument? document,
        ReadOnlyMemory<byte> rawPayload,
        LyricsFormat format,
        bool definitive)
    {
        Kind = kind;
        Document = document;
        RawPayload = rawPayload;
        Format = format;
        Definitive = definitive;
    }
```

Keep the existing `Definitive`/`Kind`/`Document`/`RawPayload`/`IsDefinitive` members. Replace the `PayloadFormat` property with:

```csharp
    /// <summary>Wire format of the payload — only meaningful for <see cref="LyricsOutcomeKind.Content"/>.</summary>
    public LyricsFormat Format { get; }
```

Update the factories (pass `LyricsFormat.Unknown` where the old code passed `null`):

```csharp
    public static LyricsProviderResult Content(
        LyricsDocument document, ReadOnlyMemory<byte> rawPayload, LyricsFormat format, bool definitive)
        => new(LyricsOutcomeKind.Content, document, rawPayload, format, definitive);

    public static readonly LyricsProviderResult DefinitiveEmpty =
        new(LyricsOutcomeKind.DefinitiveEmpty, null, default, LyricsFormat.Unknown, definitive: true);

    public static readonly LyricsProviderResult DefinitiveUnsupported =
        new(LyricsOutcomeKind.DefinitiveUnsupported, null, default, LyricsFormat.Unknown, definitive: true);

    public static readonly LyricsProviderResult Transient =
        new(LyricsOutcomeKind.Transient, null, default, LyricsFormat.Unknown, definitive: false);
```

- [ ] **Step 3.4: `LyricsCacheEntry` — `PayloadFormat`(string)→`Format`(enum)**

In `Hypersonic/Models/LyricsCacheEntry.cs`, replace the `PayloadFormat` property and its `Clone()` line:

```csharp
    /// <summary>Wire format selecting the read-time parser; <see cref="LyricsFormat.Unknown"/> for negative rows.</summary>
    public LyricsFormat Format { get; set; } = LyricsFormat.Unknown;
```

```csharp
        Format = Format,   // in Clone()
```

- [ ] **Step 3.5: Core text parsers — set `Format`, drop `SourceProvider`; delete the Netease LRC parser**

`LrcSyncedLyricsParser.cs`: change the property and the document build:

```csharp
    public LyricsFormat Format => LyricsFormat.Lrc;
```
```csharp
        var document = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Format = LyricsFormat.Lrc,
            Lines = lines,
            LanguageCode = null,
            OffsetMs = parsed.OffsetMs,
        };
```
(Remove the `SourceProvider = LrcLibLyricsConstants.ProviderId,` line.)

`LrcPlainLyricsParser.cs`:
```csharp
    public LyricsFormat Format => LyricsFormat.PlainText;
```
```csharp
        var document = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Plain,
            Format = LyricsFormat.PlainText,
            Lines = lines,
        };
```
(Remove `SourceProvider = LrcLibLyricsConstants.ProviderId,`.)

`TtmlLyricsParser.cs`: change the property to `public LyricsFormat Format => LyricsFormat.Ttml;`, add `Format = LyricsFormat.Ttml,` to the document initializer, and **remove** the `SourceProvider = TtmlLyricsConstants.Format,` line.

**Delete** `Hypersonic.Core/Services/Lyrics/NeteaseLrcLyricsParser.cs` (its LRC-line parsing is now served by `LrcSyncedLyricsParser`).

- [ ] **Step 3.6: Hypersonic parsers — Subsonic structured, Lyricify word, mapper; delete the Subsonic legacy parser**

`SubsonicStructuredLyricsParser.cs`: property → `public LyricsFormat Format => LyricsFormat.SubsonicStructured;`. In **both** document initializers, add `Format = LyricsFormat.SubsonicStructured,` and **remove** the `SourceProvider = SubsonicLyricsConstants.ProviderId,` lines.

**Delete** `Hypersonic/Services/Lyrics/SubsonicLegacyLyricsParser.cs` (plain-text parsing is now served by `LrcPlainLyricsParser` under `LyricsFormat.PlainText`).

`LyricifySyllableMapper.cs`: replace the signature and the returned document (drop `sourceId`, take the format, drop `SourceProvider`):

```csharp
    public static LyricsDocument? ToWordDocument(IReadOnlyList<ILineInfo> lines, LyricsFormat format)
    {
        // … unchanged body …

        return new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            Format = format,
            Lines = outLines,
        };
    }
```

`LyricifyWordLyricsParser.cs`: replace the string tag field with a `LyricsFormat`, drop `_sourceId`, and pass the format to the mapper:

```csharp
    private readonly LyricsFormat _format;
    private readonly LyricsRawTypes _rawType;
    private readonly ILogger _log;

    public LyricifyWordLyricsParser(LyricsFormat format, LyricsRawTypes rawType, ILoggingService logService)
    {
        _format = format;
        _rawType = rawType;
        _log = logService.GetLogger(nameof(LyricifyWordLyricsParser));
    }

    public LyricsFormat Format => _format;
```
Change the mapper call to `var document = LyricifySyllableMapper.ToWordDocument(lines, _format);` and update the log context `new { _format }` → `new { Format = _format }`.

- [ ] **Step 3.7: `ILyricifyLyricsClient` / `LyricifyLyricsClient` — `FormatTag`(string)→`Format`(enum)**

`ILyricifyLyricsClient.cs`: change the record to `public sealed record LyricifyLyricsResult(string RawText, LyricsFormat Format, LyricsCandidate Candidate);`.

`LyricifyLyricsClient.cs`: change `FetchRawAsync`'s return type to `Task<(string? Raw, LyricsFormat Format)>`, and each returned tag to the enum:
- QQ case → `LyricsFormat.Qrc`
- Netease case → `yrc present ? (yrc, LyricsFormat.Yrc) : (response?.Lrc?.Lyric, LyricsFormat.Lrc)`
- Kugou cases → `LyricsFormat.Krc`
- The final `return new LyricifyLyricsResult(rawText!, formatTag, candidate);` → `…, format, candidate);` (rename the local `formatTag`→`format`).

- [ ] **Step 3.8: Providers stamp `SourceProvider`, pass `LyricsFormat` to `Parse`/`Content`**

`SubsonicLyricsProvider.cs`:
- `FetchStructuredAsync`: `var document = _parserRegistry.Parse(LyricsFormat.SubsonicStructured, payload);` (in `SelectBestStructuredContent`), and the content line → `LyricsProviderResult.Content(best.Value.Document.WithSourceProvider(Id), best.Value.Payload, LyricsFormat.SubsonicStructured, definitive: true);`
- `FetchLegacyAsync`: `var document = _parserRegistry.Parse(LyricsFormat.PlainText, payload);` and content line → `LyricsProviderResult.Content(document.WithSourceProvider(Id), payload, LyricsFormat.PlainText, definitive: structuredDefinitive);`

`LrcLibLyricsProvider.cs` — `MapRecord`:
```csharp
        if (!string.IsNullOrWhiteSpace(record.SyncedLyrics))
        {
            anyFieldPresent = true;
            var payload = Encoding.UTF8.GetBytes(record.SyncedLyrics);
            var doc = _parserRegistry.Parse(LyricsFormat.Lrc, payload);
            if (doc is not null)
            {
                return LyricsProviderResult.Content(doc.WithSourceProvider(Id), payload, LyricsFormat.Lrc, definitive: true);
            }
        }

        if (!string.IsNullOrWhiteSpace(record.PlainLyrics))
        {
            anyFieldPresent = true;
            var payload = Encoding.UTF8.GetBytes(record.PlainLyrics);
            var doc = _parserRegistry.Parse(LyricsFormat.PlainText, payload);
            if (doc is not null)
            {
                return LyricsProviderResult.Content(doc.WithSourceProvider(Id), payload, LyricsFormat.PlainText, definitive: true);
            }
        }
```

`AmllTtmlLyricsProvider.cs`: `var document = _parserRegistry.Parse(LyricsFormat.Ttml, bytes);` and the return → `LyricsProviderResult.Content(document.WithSourceProvider(Id), bytes, LyricsFormat.Ttml, definitive: true);`

`LyricifyLyricsProviderBase.cs`: `var document = _parserRegistry.Parse(result.Format, bytes);` and the return → `LyricsProviderResult.Content(document.WithSourceProvider(Id), bytes, result.Format, definitive: true);`

- [ ] **Step 3.9: `LocalFileLyricsSource` — dispatch by enum**

In `LocalFileLyricsSource.cs`, change the parse call to `var document = _registry.Parse(LyricsFormat.Ttml, bytes);`. **Leave** the file-extension usage `songId + "." + TtmlLyricsConstants.Format` unchanged (`TtmlLyricsConstants.Format` = `"ttml"` remains the override file extension). Add `using Hypersonic.Core.Models.Lyrics;` if not present.

- [ ] **Step 3.10: `LyricsService` — extension map, write paths, and preserve `Format` on overlay**

`ExtensionForFormat` → key on the enum:
```csharp
    internal static string ExtensionForFormat(LyricsFormat format) => format switch
    {
        LyricsFormat.SubsonicStructured => ".json",
        LyricsFormat.PlainText => ".txt",
        LyricsFormat.Lrc => ".lrc",
        LyricsFormat.Ttml => ".ttml",
        LyricsFormat.Qrc => ".qrc",
        LyricsFormat.Krc => ".krc",
        LyricsFormat.Yrc => ".yrc",
        _ => ".txt",
    };
```

`WritePositiveAsync` — the entry now carries the typed format (the `winner.PayloadFormat!` references become `winner.Format`):
```csharp
                FileName = keyHash + ExtensionForFormat(winner.Format),
                Length = winner.RawPayload.Length,
                SourceProvider = document.SourceProvider ?? string.Empty,
                Format = winner.Format,
```

`WriteNegativeAsync` — the format column is `Unknown` for negative rows:
```csharp
                SourceProvider = _providers.Length > 0 ? _providers[0].Id : string.Empty,
                Format = LyricsFormat.Unknown,
```
(Remove the old `PayloadFormat = string.Empty,` line.)

`TryReadCacheAsync` — dispatch by the entry's typed format:
```csharp
        var document = _parserRegistry.Parse(payload.Entry.Format, payload.Bytes);
```
(and the log context `payload.Entry.PayloadFormat` → `payload.Entry.Format`.)

**`OverlayRowMetadata` — preserve `Format` in the rebuild** (the rebuilt document must not drop the parser-stamped format):
```csharp
        return new LyricsDocument
        {
            SyncKind = document.SyncKind,
            Format = document.Format,
            Lines = document.Lines,
            SourceProvider = source,
            LanguageCode = language,
            OffsetMs = offset,
        };
```

- [ ] **Step 3.11: `SqliteLyricsCacheStore` — map the typed `Format` (column name stays `PayloadFormat` for now)**

In `SqliteLyricsCacheStore.cs`, keep the SQL column named `PayloadFormat` (Task 4 renames it), but change the C# mapping:

`UpsertEntryLockedAsync` — store the enum name:
```csharp
        command.Parameters.AddWithValue("$payloadFormat", entry.Format.ToString());
```

`ReadEntry` — parse the enum name with an `Unknown` fallback (old string tags like `"netease-lrc"` fall through to `Unknown` → dispatch miss → self-heal re-fetch):
```csharp
            Format = Enum.TryParse<LyricsFormat>(reader.GetString(offset + 3), out var fmt) ? fmt : LyricsFormat.Unknown,
```
Add `using Hypersonic.Core.Models.Lyrics;` if not already present.

- [ ] **Step 3.12: `App.xaml.cs` — parser registrations**

In the lyrics DI block, **remove** these two lines:
```csharp
            services.AddSingleton<ILyricsPayloadParser, SubsonicLegacyLyricsParser>();
```
```csharp
            services.AddSingleton<ILyricsPayloadParser, NeteaseLrcLyricsParser>();
```
Replace the three `LyricifyWordLyricsParser` factory registrations with (drop `sourceId`, pass `LyricsFormat`):
```csharp
            services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
                LyricsFormat.Qrc, Lyricify.Lyrics.Models.LyricsRawTypes.Qrc,
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
                LyricsFormat.Krc, Lyricify.Lyrics.Models.LyricsRawTypes.Krc,
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
                LyricsFormat.Yrc, Lyricify.Lyrics.Models.LyricsRawTypes.Yrc,
                sp.GetRequiredService<ILoggingService>()));
```
Add `using Hypersonic.Core.Models.Lyrics;` to `App.xaml.cs` if not present.

- [ ] **Step 3.13: Update the affected tests (exhaustive)**

Apply the rule: **parser tests assert `Format` (never `SourceProvider`, which the parser no longer stamps); provider tests assert the provider stamps `SourceProvider`; every `Content(...)` / `LyricsCacheEntry` / `Parse(...)` / `ExtensionForFormat(...)` reference passes a `LyricsFormat`; fakes implementing `ILyricsPayloadParser`/`ILyricsParserRegistry` adopt the enum signatures.** Add `using Hypersonic.Core.Models.Lyrics;` wherever `LyricsFormat` is newly referenced.

**Core.Tests — parsers + registry:**
- `Hypersonic.Core.Tests/Services/Lyrics/LrcSyncedLyricsParserTests.cs`: `FormatTag_IsLrclibSynced` (~:16) → assert `parser.Format == LyricsFormat.Lrc`; the doc test asserting `doc.SourceProvider == "lrclib"` (~:25) → assert `doc.Format == LyricsFormat.Lrc` and **remove** the `SourceProvider` assertion.
- `LrcPlainLyricsParserTests.cs`: `FormatTag_IsLrclibPlain` (~:16) → `parser.Format == LyricsFormat.PlainText`; `SourceProvider` assert (~:25) → `doc.Format == LyricsFormat.PlainText`.
- `TtmlLyricsParserTests.cs`: `FormatTag_IsTtml` (:35) → `parser.Format == LyricsFormat.Ttml`; rename `SourceProvider_IsTtml` (:67) → `Format_IsTtml` asserting `Parse(TwoLineWordSynced)!.Format == LyricsFormat.Ttml`.
- **Delete** `NeteaseLrcLyricsParserTests.cs` (parser deleted; LRC-line coverage remains in `LrcSyncedLyricsParserTests`).
- `LyricsParserRegistryTests.cs`: the inline `FakeParser` → `public LyricsFormat Format { get; }` (was `string FormatTag`) with a `LyricsFormat` ctor arg; every `registry.Parse("a"/"b"/…, …)` → `registry.Parse(LyricsFormat.X, …)`; **redesign** `Parse_EmptyFormatTag_ReturnsNull` (:55) — there is no empty enum, so rename to `Parse_UnregisteredFormat_ReturnsNull`, register a fake for one format (e.g. `LyricsFormat.Lrc`), then `Parse(LyricsFormat.Ttml, …)` and assert `null`.

**Hypersonic.Tests — parsers:**
- `SubsonicLyricsParsersTests.cs`: **delete** the tests exercising the (deleted) `SubsonicLegacyLyricsParser`; for the structured parser replace `Assert.AreEqual("subsonic", doc.SourceProvider)` → `Assert.AreEqual(LyricsFormat.SubsonicStructured, doc.Format)`.
- `LyricifyWordLyricsParserTests.cs`: construct via the new 3-arg ctor `new LyricifyWordLyricsParser(LyricsFormat.Qrc, LyricsRawTypes.Qrc, log)` (drop the id arg) — including the `QrcParser()` helper (~:22) and `FormatTag_IsQrc` (~:25 → `.Format == LyricsFormat.Qrc`); in `Qrc_ParsesToWordDocument` (~:35) **remove** the `doc.SourceProvider == "qq"` assertion (now null) and assert `doc.Format == LyricsFormat.Qrc`; **delete** `SourceId_FlowsThroughRegardlessOfFormat` (~:47) — the `sourceId` param it verifies no longer exists.

**Hypersonic.Tests — providers (assert the provider stamps `SourceProvider`):**
- `LrcLibLyricsProviderTests.cs`: `Assert.AreEqual("lrclib-synced"/"lrclib-plain", r.PayloadFormat)` (~:40/:51) → `Assert.AreEqual(LyricsFormat.Lrc / LyricsFormat.PlainText, r.Format)`; add `Assert.AreEqual("lrclib", r.Document!.SourceProvider)`.
- `SubsonicLyricsProviderTests.cs`: it constructs `SubsonicLegacyLyricsParser` (~:20/:30, deleted) — repoint to `new LrcPlainLyricsParser(log)` (Core, `LyricsFormat.PlainText`); `r.PayloadFormat` asserts (~:87/:142/:255) → `r.Format`; keep/confirm `r.Document!.SourceProvider == "subsonic"`.
- `AmllTtmlLyricsProviderTests.cs`: `Assert.AreEqual("ttml", r.PayloadFormat)` (~:47) → `Assert.AreEqual(LyricsFormat.Ttml, r.Format)`. (The new AMLL identity test is Step 3.14.)
- `LyricifyLyricsProviderTests.cs`: the 4-arg `LyricifyWordLyricsParser` ctor (~:33) → 3-arg; `LyricifyLyricsResult(sample, "qrc", …)` fakes (~:66/:76) → `LyricifyLyricsResult(sample, LyricsFormat.Qrc, …)`; `r.PayloadFormat` (~:81) → `r.Format`; add `r.Document!.SourceProvider == "qq"` (provider now stamps identity).

**Hypersonic.Tests — service + cache + registration:**
- `LyricsServiceTests.cs`:
  - Replace the `SubsonicLegacyLyricsParser _legacyParser` field + its construction (:27/:40, deleted class) with `LrcPlainLyricsParser _plainParser = new LrcPlainLyricsParser(log)`; update `_registry = new LyricsParserRegistry([new SubsonicStructuredLyricsParser(log), _plainParser], log)`.
  - `LegacyContent(...)` helper (:62-67): `var doc = _plainParser.Parse(payload)!; return LyricsProviderResult.Content(doc, payload, LyricsFormat.PlainText, definitive);`.
  - `ExtensionForFormat("ttml")` (:535) → `ExtensionForFormat(LyricsFormat.Ttml)`.
  - `PayloadFormat = "metadata-poor"` (:238) / `PayloadFormat = "x"` (:277) on seeded `LyricsCacheEntry` → `Format = LyricsFormat.Lrc` (or `LyricsFormat.Unknown` if the seeded row is a negative/empty row).
  - The `StubRegistry` fake (:615) — `public LyricsDocument? Parse(string formatTag, …)` → `Parse(LyricsFormat format, …)`; update its internal lookup to key on `LyricsFormat`.
  - Any remaining `LyricsProviderResult.Content(doc, bytes, "…tag…", def)` (e.g. :315/:326/:512) → pass the matching `LyricsFormat`. Inline docs setting `SourceProvider` directly are unchanged; keep the winner/tiebreak `SourceProvider` assertions.
- `SqliteLyricsCacheStoreTests.cs`: every `PayloadFormat = "<tag>"` on a constructed `LyricsCacheEntry` → `Format = LyricsFormat.<X>`; the negative-row `PayloadFormat = string.Empty` cases (~:173/:274) → `Format = LyricsFormat.Unknown`; round-trip assertions compare `read.Format`.
- `LyricsCacheFormatTests.cs`: the `LyricsService.ExtensionForFormat(QQLyricsConstants.QrcFormat)` etc. calls (~:12-15) → `ExtensionForFormat(LyricsFormat.Qrc)` and the other enum values; update expected extensions if the file asserts them.
- `LyricsProviderRegistrationTests.cs` (**updated here in Task 3** — it references deleted types): in `BuildLyricsSubset()` change the three `LyricifyWordLyricsParser` factories to the 3-arg enum ctor (`LyricsFormat.Qrc/.Krc/.Yrc`), **remove** the `NeteaseLrcLyricsParser` registration (:31, deleted) and instead register the shared LRC parser (`services.AddSingleton<ILyricsPayloadParser, LrcSyncedLyricsParser>();`) so the registry can resolve `LyricsFormat.Lrc`; rewrite `Registry_ResolvesNewFormatTags` (:54) to `registry.Parse(LyricsFormat.Lrc, Encoding.UTF8.GetBytes("[00:01.00]hi"))`, assert `doc is not null` and `doc.SyncKind == LyricsSyncKind.Line`, and **remove** the `SourceProvider == "netease"` assertion (the shared parser no longer stamps identity). `LyricifyProvider_ResolveInQQNeteaseKugouOrder` is unaffected.
- `LyricsFallbackPolicyTests.cs`: the `new LyricsDocument { …, SourceProvider = source }` helper is unaffected; any `Content(…, "tag", …)` → the enum.
- `Hypersonic.Tests/Fakes/FakeLyricsCacheStore.cs`: if it constructs/copies `LyricsCacheEntry.PayloadFormat`, change to `Format`; if it stores entries opaquely, no change.

- [ ] **Step 3.14: Add the AMLL identity regression test**

The whole point of the ownership shift — verify AMLL's returned document is attributed to the provider, not the format. In `Hypersonic.Tests/Services/Lyrics/AmllTtmlLyricsProviderTests.cs` add (adapt to the file's existing fixture/setup for a successful fetch):

```csharp
[TestMethod]
public async Task FetchAsync_Content_StampsAmllProviderIdAndTtmlFormat()
{
    // Arrange: index ready + a resolvable file + CDN returning valid word-synced TTML
    // (reuse the file's existing success-path fixture).
    var result = await _provider.FetchAsync(_query, CancellationToken.None);

    Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
    Assert.AreEqual("amll-ttml-db", result.Document!.SourceProvider); // identity, not "ttml"
    Assert.AreEqual(LyricsFormat.Ttml, result.Document!.Format);
}
```

- [ ] **Step 3.15: Build and run the full suite**

Run:
```bash
dotnet build Hypersonic.sln -p:Platform=x64
dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj
dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64
```
Expected: build succeeds; all tests pass (deleted parser tests gone, updated assertions green, AMLL test green).

- [ ] **Step 3.16: Commit**

```bash
git add -A
git commit -m "refactor(lyrics): unify payload format into typed LyricsFormat; parser owns Format, provider owns SourceProvider"
```

---

### Task 4: Cache schema v2 — rename column, discard old rows

**Files:**
- Modify: `Hypersonic/Services/SqliteLyricsCacheStore.cs`
- Test: `Hypersonic.Tests/Services/Lyrics/SqliteLyricsCacheStoreTests.cs` (append)

**Interfaces:**
- Consumes: `LyricsCacheEntry.Format` (Task 3), the base `MigrationStep(int TargetVersion, string Sql)` record.
- Produces: cache DB schema `v2` with a `Format` column; a `v2` migration step that drops+recreates the table (existing rows discarded, re-fetched lazily).

- [ ] **Step 1: Write the failing test**

Append to `SqliteLyricsCacheStoreTests.cs`. The `Format` round-trip is already covered by the existing store tests once Step 3.13 updates their `PayloadFormat = "…"` seeds to `Format = LyricsFormat.X`; the new test here is the v1→v2 discard. The fixture's DB lives at `<_cacheRoot>/cache.db` (the store roots its cache folder at `_cacheParent` and appends the `"Lyrics"` subfolder, which is `_cacheRoot`), and `CreateStore()` is the fixture's store factory (verify both against the file's `Setup`/helpers).

```csharp
[TestMethod]
public async Task LegacyV1Row_IsDiscardedOnUpgrade()
{
    // Hand-craft a v1 DB (old PayloadFormat column + a legacy-tag row) where the store will open it.
    var dbPath = Path.Combine(_cacheRoot, "cache.db");
    await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        await connection.OpenAsync();
        await using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE Entries (
                KeyHash TEXT PRIMARY KEY NOT NULL, FileName TEXT NOT NULL, Length INTEGER NOT NULL,
                SourceProvider TEXT NOT NULL, PayloadFormat TEXT NOT NULL, SyncKind TEXT NOT NULL,
                LanguageCode TEXT, OffsetMs INTEGER, CreatedAtUtc INTEGER NOT NULL,
                LastAccessUtc INTEGER NOT NULL, ExpiresAtUtc INTEGER NOT NULL) WITHOUT ROWID;
            INSERT INTO Entries VALUES ('deadbeef','x.lrc',3,'netease','netease-lrc','Line',NULL,NULL,0,0,999999999999);
            PRAGMA user_version = 1;
            """;
        await create.ExecuteNonQueryAsync();
    }
    SqliteConnection.ClearAllPools(); // release the handle so the store can reopen the file

    // Opening the store triggers the v1→v2 migration step (DROP + recreate).
    var store = CreateStore();
    var read = await store.GetEntryAsync("deadbeef", CancellationToken.None);
    Assert.IsNull(read); // discarded — a later fetch repopulates under the v2 schema
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LegacyV1Row_IsDiscardedOnUpgrade"`
Expected: FAIL — with `CurrentSchemaVersion` still `1` and no `v2` step, the store leaves the v1 row intact, so `read` is non-null.

- [ ] **Step 3: Bump the version, add the discard step, rename the column**

In `SqliteLyricsCacheStore.cs`:

```csharp
    protected override int CurrentSchemaVersion => 2;

    protected override IReadOnlyList<MigrationStep> MigrationSteps =>
    [
        // v1→v2: the format column changed type/meaning (string tags → LyricsFormat) and old rows
        // carry the pre-fix AMLL identity — none map cleanly forward, so discard and re-fetch lazily.
        new MigrationStep(2, "DROP TABLE IF EXISTS Entries;\n" + CreateEntriesTableSql + "\n" + CreateLastAccessIndexSql),
    ];
```

Rename the column in `CreateEntriesTableSql` (`PayloadFormat` → `Format`):
```csharp
            SourceProvider TEXT    NOT NULL,
            Format         TEXT    NOT NULL,
            SyncKind       TEXT    NOT NULL,
```

Rename it in every `SELECT` list (`GetEntryLockedAsync`, `GetEvictionCandidatesLockedAsync`, `EnumerateAllEntriesLockedAsync`): `SourceProvider, PayloadFormat, SyncKind` → `SourceProvider, Format, SyncKind`.

Rename it in `UpsertEntryLockedAsync`'s INSERT column list, VALUES placeholder, and the `ON CONFLICT` set: `PayloadFormat` → `Format`, `$payloadFormat` → `$format`. Update the parameter line:
```csharp
        command.Parameters.AddWithValue("$format", entry.Format.ToString());
```

(`ReadEntry`'s `offset + 3` position is unchanged — it still reads the third-after-`SourceProvider` column, now named `Format`.)

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~SqliteLyricsCacheStoreTests"`
Expected: PASS (round-trip + legacy-discard, plus the existing store tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/Services/SqliteLyricsCacheStore.cs Hypersonic.Tests/Services/Lyrics/SqliteLyricsCacheStoreTests.cs
git commit -m "feat(lyrics): cache schema v2 — Format column + discard-on-upgrade migration"
```

---

# Stage 2 — Source Badge on Now Playing

### Task 5: Add `DisplayName` to `ILyricsProvider` and every provider

**Files:**
- Modify: `Hypersonic.Core/Contracts/Lyrics/ILyricsProvider.cs`
- Modify: `Hypersonic/Services/Lyrics/{SubsonicLyricsProvider,LrcLibLyricsProvider,AmllTtmlLyricsProvider,LyricifyLyricsProviderBase,QQMusicLyricsProvider,NeteaseLyricsProvider,KugouLyricsProvider}.cs`
- Modify: `Hypersonic.Tests/Services/Lyrics/LyricsServiceTests.cs` (the `FakeLyricsProvider` fake gains `DisplayName`)
- Create: `Hypersonic.Tests/Services/Lyrics/LyricsProviderDisplayNameTests.cs`

**Interfaces:**
- Produces: `string ILyricsProvider.DisplayName { get; }` — human brand name per provider.

- [ ] **Step 1: Write the failing test**

`DisplayName` is a dependency-free constant getter, so construct each provider directly (no DI graph): each ctor only dereferences `logService.GetLogger(...)` and, where present, `options.Value`, so a real `NullLoggingService` + real `Options.Create(...)` suffice and `null!` is safe for the deps the getter never touches.

```csharp
// Hypersonic.Tests/Services/Lyrics/LyricsProviderDisplayNameTests.cs
using Hypersonic.Models;
using Hypersonic.Services.Lyrics;
using Hypersonic.Tests.Fakes;

using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsProviderDisplayNameTests
{
    private static readonly NullLoggingService Log = new();

    [TestMethod]
    public void Subsonic_DisplayName()
        => Assert.AreEqual("Subsonic", new SubsonicLyricsProvider(null!, null!, Log).DisplayName);

    [TestMethod]
    public void LrcLib_DisplayName()
        => Assert.AreEqual("LrcLib", new LrcLibLyricsProvider(null!, null!, Log).DisplayName);

    [TestMethod]
    public void Amll_DisplayName()
        => Assert.AreEqual("AMLL",
            new AmllTtmlLyricsProvider(null!, null!, null!, Options.Create(new AmllLyricsOptions()), Log).DisplayName);

    [TestMethod]
    public void QQ_DisplayName()
        => Assert.AreEqual("QQ Music",
            new QQMusicLyricsProvider(null!, null!, Options.Create(new QQLyricsOptions()), Log).DisplayName);

    [TestMethod]
    public void Netease_DisplayName()
        => Assert.AreEqual("Netease",
            new NeteaseLyricsProvider(null!, null!, Options.Create(new NeteaseLyricsOptions()), Log).DisplayName);

    [TestMethod]
    public void Kugou_DisplayName()
        => Assert.AreEqual("Kugou",
            new KugouLyricsProvider(null!, null!, Options.Create(new KugouLyricsOptions()), Log).DisplayName);
}
```

(Confirm the exact ctor arg order against each provider file and adjust the `null!`/`Options.Create` slots accordingly.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsProviderDisplayNameTests"`
Expected: FAIL (build error — `DisplayName` not on `ILyricsProvider`).

- [ ] **Step 3: Add the interface member**

In `ILyricsProvider.cs`, add:
```csharp
    /// <summary>Human-facing brand name for this source (e.g. "LrcLib"). Not localized.</summary>
    string DisplayName { get; }
```

- [ ] **Step 4: Implement on every provider**

- `SubsonicLyricsProvider.cs`: `public string DisplayName => "Subsonic";`
- `LrcLibLyricsProvider.cs`: `public string DisplayName => "LrcLib";`
- `AmllTtmlLyricsProvider.cs`: `public string DisplayName => "AMLL";`
- `LyricifyLyricsProviderBase.cs`: add `public abstract string DisplayName { get; }` (next to `public abstract string Id { get; }`).
- `QQMusicLyricsProvider.cs`: `public override string DisplayName => "QQ Music";`
- `NeteaseLyricsProvider.cs`: `public override string DisplayName => "Netease";`
- `KugouLyricsProvider.cs`: `public override string DisplayName => "Kugou";`
- `Hypersonic.Tests/Services/Lyrics/LyricsServiceTests.cs` — the `FakeLyricsProvider` fake (~:624) implements only `Id` + `FetchAsync`; add `public string DisplayName => "subsonic";` (any value) so it still satisfies `ILyricsProvider`. (No other test-side `ILyricsProvider` fake exists; the `FakeProvider` in the catalog test already declares `DisplayName`.)

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS (the new `LyricsProviderDisplayNameTests`, plus the whole app suite — which also confirms `FakeLyricsProvider` still compiles).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(lyrics): add DisplayName to lyrics providers"
```

---

### Task 6: `ILyricsSourceCatalog` + registration

**Files:**
- Create: `Hypersonic/Contracts/Services/ILyricsSourceCatalog.cs`
- Create: `Hypersonic/Services/LyricsSourceCatalog.cs`
- Modify: `Hypersonic/App.xaml.cs`
- Test: `Hypersonic.Tests/Services/Lyrics/LyricsSourceCatalogTests.cs`

**Interfaces:**
- Consumes: `ILyricsProvider.Id` + `.DisplayName` (Task 5).
- Produces: `string? ILyricsSourceCatalog.DisplayNameFor(string? providerId)` — display name for a known id; `null` for unknown or null id.

- [ ] **Step 1: Write the failing test**

```csharp
// Hypersonic.Tests/Services/Lyrics/LyricsSourceCatalogTests.cs
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsSourceCatalogTests
{
    private sealed class FakeProvider(string id, string displayName) : ILyricsProvider
    {
        public string Id => id;
        public string DisplayName => displayName;
        public Task<LyricsProviderResult> FetchAsync(LyricsQuery q, CancellationToken ct)
            => Task.FromResult(LyricsProviderResult.Transient);
    }

    private static ILyricsSourceCatalog Catalog()
        => new LyricsSourceCatalog(new ILyricsProvider[]
        {
            new FakeProvider("lrclib", "LrcLib"),
            new FakeProvider("amll-ttml-db", "AMLL"),
        });

    [TestMethod]
    public void DisplayNameFor_KnownId_ReturnsName()
        => Assert.AreEqual("LrcLib", Catalog().DisplayNameFor("lrclib"));

    [TestMethod]
    public void DisplayNameFor_UnknownId_ReturnsNull()
        => Assert.IsNull(Catalog().DisplayNameFor("ttml"));

    [TestMethod]
    public void DisplayNameFor_Null_ReturnsNull()
        => Assert.IsNull(Catalog().DisplayNameFor(null));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsSourceCatalogTests"`
Expected: FAIL (build error — types missing).

- [ ] **Step 3: Create the interface and implementation**

```csharp
// Hypersonic/Contracts/Services/ILyricsSourceCatalog.cs
namespace Hypersonic.Contracts.Services;

/// <summary>Resolves a lyrics provider id (the value in <c>LyricsDocument.SourceProvider</c>) to its
/// human display name. Built once from the registered providers.</summary>
public interface ILyricsSourceCatalog
{
    /// <summary>Display name for <paramref name="providerId"/>, or <c>null</c> when the id is unknown or null.</summary>
    string? DisplayNameFor(string? providerId);
}
```

```csharp
// Hypersonic/Services/LyricsSourceCatalog.cs
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;

namespace Hypersonic.Services;

/// <summary>Static id→DisplayName map built from the registered <see cref="ILyricsProvider"/> set.</summary>
internal sealed class LyricsSourceCatalog : ILyricsSourceCatalog
{
    private readonly Dictionary<string, string> _names;

    public LyricsSourceCatalog(IEnumerable<ILyricsProvider> providers)
        => _names = providers.ToDictionary(p => p.Id, p => p.DisplayName, StringComparer.Ordinal);

    public string? DisplayNameFor(string? providerId)
        => providerId is not null && _names.TryGetValue(providerId, out var name) ? name : null;
}
```

- [ ] **Step 4: Register in DI**

In `App.xaml.cs`, after the `ILyricsService` registration (`services.AddSingleton<ILyricsService, LyricsService>();`), add:
```csharp
            services.AddSingleton<ILyricsSourceCatalog, LyricsSourceCatalog>();
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsSourceCatalogTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(lyrics): add ILyricsSourceCatalog resolving provider id to display name"
```

---

### Task 7: `NowPlayingViewModel` derived source properties

**Files:**
- Modify: `Hypersonic/ViewModels/NowPlayingViewModel.cs`
- Test: `Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs` (append)

**Interfaces:**
- Consumes: `ILyricsSourceCatalog.DisplayNameFor` (Task 6), `LyricsDocument.SourceProvider`, `LyricsLoadState`.
- Produces: `string? LyricsSourceName`, `bool HasLyricsSource`, `string? LyricsSourceTooltip` on `NowPlayingViewModel`.

- [ ] **Step 1: Write the failing tests**

Append to `NowPlayingViewModelTests.cs`. The file builds the VM through two static `Build(...)` overloads (both call the internal synchronous ctor); Step 4 threads the catalog through them. Add a minimal fake (needs `using Hypersonic.Contracts.Services;`):

```csharp
private sealed class FakeSourceCatalog : ILyricsSourceCatalog
{
    public string? DisplayNameFor(string? id) => id switch { "lrclib" => "LrcLib", _ => null };
}
```

```csharp
[TestMethod]
public void LyricsSource_LoadedWithKnownProvider_ExposesNameAndTooltip()
{
    var vm = Build(new FakePlaybackService(), _ => Task.FromResult<LyricsDocument?>(null)); // fixture helper; Step 4 wires FakeSourceCatalog into Build
    vm.CurrentLyrics = new LyricsDocument { SyncKind = LyricsSyncKind.Line, SourceProvider = "lrclib" };
    vm.LyricsLoadState = LyricsLoadState.Loaded;

    Assert.IsTrue(vm.HasLyricsSource);
    Assert.AreEqual("LrcLib", vm.LyricsSourceName);
    Assert.AreEqual("Lyrics from LrcLib", vm.LyricsSourceTooltip);
}

[TestMethod]
public void LyricsSource_Loading_IsHidden()
{
    var vm = Build(new FakePlaybackService(), _ => Task.FromResult<LyricsDocument?>(null));
    vm.CurrentLyrics = new LyricsDocument { SyncKind = LyricsSyncKind.Line, SourceProvider = "lrclib" };
    vm.LyricsLoadState = LyricsLoadState.Loading;
    Assert.IsFalse(vm.HasLyricsSource);
    Assert.IsNull(vm.LyricsSourceTooltip);
}

[TestMethod]
public void LyricsSource_UnknownProvider_IsHidden()
{
    var vm = Build(new FakePlaybackService(), _ => Task.FromResult<LyricsDocument?>(null));
    vm.CurrentLyrics = new LyricsDocument { SyncKind = LyricsSyncKind.Word, SourceProvider = "ttml" };
    vm.LyricsLoadState = LyricsLoadState.Loaded;
    Assert.IsFalse(vm.HasLyricsSource); // unknown id (e.g. dev override) → nothing to attribute
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~NowPlayingViewModelTests"`
Expected: FAIL (build error — ctor arg + members missing).

- [ ] **Step 3: Inject the catalog and add the derived properties**

In `NowPlayingViewModel.cs`, add the field and constructor parameter to **both** constructors, positioned **immediately after `ILoggingService logService`**:

```csharp
    private readonly ILyricsSourceCatalog _sourceCatalog;
```
- Public ctor: append `, ILyricsSourceCatalog sourceCatalog` after `ILoggingService logService`; assign `_sourceCatalog = sourceCatalog;`.
- Internal ctor: insert `ILyricsSourceCatalog sourceCatalog` after `ILoggingService logService` and **before** `bool synchronous`; assign `_sourceCatalog = sourceCatalog;`.
- Add `using Hypersonic.Contracts.Services;` if not already present.

Add `[NotifyPropertyChangedFor]` to the two source `[ObservableProperty]` declarations so the computed getters re-raise:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnableKaraokeWipe))]
    [NotifyPropertyChangedFor(nameof(LyricsSourceName))]
    [NotifyPropertyChangedFor(nameof(HasLyricsSource))]
    [NotifyPropertyChangedFor(nameof(LyricsSourceTooltip))]
    public partial LyricsDocument? CurrentLyrics { get; set; }
```
```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLyricsSource))]
    [NotifyPropertyChangedFor(nameof(LyricsSourceTooltip))]
    public partial LyricsLoadState LyricsLoadState { get; set; } = LyricsLoadState.NoTrack;
```

Add the computed properties (e.g. below `EnableKaraokeWipe`):

```csharp
    /// <summary>Display name of the source that served the current lyrics, or null when unknown.</summary>
    public string? LyricsSourceName => _sourceCatalog.DisplayNameFor(CurrentLyrics?.SourceProvider);

    /// <summary>Whether to show the source info glyph — only for loaded lyrics from a known provider.</summary>
    public bool HasLyricsSource => LyricsLoadState == LyricsLoadState.Loaded && LyricsSourceName is not null;

    /// <summary>Tooltip text for the source glyph, e.g. "Lyrics from LrcLib".</summary>
    public string? LyricsSourceTooltip => HasLyricsSource ? $"Lyrics from {LyricsSourceName}" : null;
```

- [ ] **Step 4: Thread the catalog through the test `Build(...)` helpers**

`NowPlayingViewModelTests` builds the VM via two static `Build(...)` overloads (both call the internal ctor). Add `new FakeSourceCatalog()` to each, positioned after `new NullLoggingService()` and before `synchronous: true` (matching the internal ctor's new param order):

```csharp
private static NowPlayingViewModel Build(FakePlaybackService svc, Func<LyricsQuery, Task<LyricsDocument?>> handler, Action<Action>? enqueueOverride = null)
    => new(svc, new FakeLyricsService(handler), Opts, new NullLoggingService(), new FakeSourceCatalog(), synchronous: true, enqueueOverride: enqueueOverride);

private static NowPlayingViewModel Build(FakePlaybackService svc, FakeLyricsService service, Action<Action>? enqueueOverride = null)
    => new(svc, service, Opts, new NullLoggingService(), new FakeSourceCatalog(), synchronous: true, enqueueOverride: enqueueOverride);
```

The real app needs no change — the DI container supplies `ILyricsSourceCatalog` automatically once Task 6 registered it.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~NowPlayingViewModelTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(lyrics): expose lyrics source name/tooltip on NowPlayingViewModel"
```

---

### Task 8: Info glyph on `NowPlayingPage`

**Files:**
- Modify: `Hypersonic/Views/NowPlayingPage.xaml`

**Interfaces:**
- Consumes: `ViewModel.HasLyricsSource`, `ViewModel.LyricsSourceTooltip` (Task 7); the page's existing `BoolToVisibilityConverter` resource.

- [ ] **Step 1: Group the info glyph with the refresh button**

In `NowPlayingPage.xaml`, replace the existing `<Button x:Name="RefreshLyricsButton" …>…</Button>` (currently anchored top-right of `LyricsSurface`) with a horizontal `StackPanel` holding the new info glyph followed by the unchanged refresh button. Move the alignment/margin from the button onto the panel:

```xml
<StackPanel Orientation="Horizontal"
            HorizontalAlignment="Right" VerticalAlignment="Top"
            Margin="0,4,4,0" Spacing="0">
    <!-- Source provenance: shown only for loaded lyrics from a known provider. Focusable
         (TransparentButtonStyle, no command) so the tooltip is reachable by keyboard/touch. -->
    <Button x:Name="LyricsSourceInfoButton"
            Width="36" Height="36"
            Style="{StaticResource TransparentButtonStyle}"
            Visibility="{x:Bind ViewModel.HasLyricsSource, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
            ToolTipService.ToolTip="{x:Bind ViewModel.LyricsSourceTooltip, Mode=OneWay}">
        <FontIcon Glyph="&#xE946;" FontSize="16"/>
    </Button>

    <!-- Manual refresh (existing) — Margin/alignment now on the parent StackPanel. -->
    <Button x:Name="RefreshLyricsButton"
            Width="36" Height="36"
            Style="{StaticResource TransparentButtonStyle}"
            Command="{x:Bind ViewModel.RefreshLyricsCommand}"
            IsEnabled="{x:Bind Player.HasTrack, Mode=OneWay}"
            ToolTipService.ToolTip="Refresh lyrics">
        <Grid>
            <FontIcon Glyph="&#xE72C;" FontSize="16"
                      Visibility="{x:Bind ViewModel.RefreshLyricsCommand.IsRunning, Mode=OneWay, Converter={StaticResource InverseBoolToVisibilityConverter}}"/>
            <ProgressRing Width="16" Height="16"
                          IsActive="{x:Bind ViewModel.RefreshLyricsCommand.IsRunning, Mode=OneWay}"
                          Visibility="{x:Bind ViewModel.RefreshLyricsCommand.IsRunning, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"/>
        </Grid>
    </Button>
</StackPanel>
```

- [ ] **Step 2: Build**

Run: `dotnet build Hypersonic.sln -p:Platform=x64`
Expected: build succeeds (XAML compiles; bindings resolve to the Task 7 members).

- [ ] **Step 3: Manual verification**

Deploy/run the app (VS F5 or MSIX). Play a track with lyrics → an ⓘ glyph appears top-right of the lyrics area, left of the refresh button; hovering (or focusing) shows "Lyrics from <Source>". Play a track with no lyrics → the glyph is hidden. Confirm the refresh button still spins on manual refresh.

- [ ] **Step 4: Commit**

```bash
git add Hypersonic/Views/NowPlayingPage.xaml
git commit -m "feat(lyrics): show lyrics source info glyph on Now Playing"
```

---

## Self-Review

**Spec coverage** — every design section maps to a task:
- `LyricsFormat` enum incl. `SubsonicStructured` → Task 1.
- `LyricsDocument.Format` + copy helper → Task 2.
- Parser dedup, typed merge, ownership shift (parser owns `Format`+`SyncKind`, provider owns `SourceProvider`), AMLL fix, `OverlayRowMetadata` preserving `Format` → Task 3.
- Cache column rename + `v2` discard migration → Task 4.
- `ILyricsProvider.DisplayName` → Task 5. Catalog → Task 6. VM derived props → Task 7. Info glyph → Task 8.

**Placeholder scan** — no TBD/TODO; new code is complete. Step 3.13 is an exhaustive per-file test-update list (derived from a full missed-caller audit). The `SubsonicStructured` naming was resolved with the user (kept).

**Type consistency** — `LyricsFormat` member names are identical across Tasks 1/3/4/7; `Format` (not `FormatTag`/`PayloadFormat`) is used uniformly after Task 3; `WithSourceProvider` (Task 2) is the exact helper called by providers in Task 3.8; `DisplayNameFor` (Task 6) is the exact method used in Task 7.

**Known atomicity note:** Task 3 is a single non-splittable compile unit (interface/model signature changes ripple through all implementors at once). Intermediate builds fail by design; only Step 3.15 must be green.

**Post-review revisions (Opus review pass):** exhaustive Step 3.13 now covers `LyricsCacheFormatTests`, `LyricsServiceTests` (`ExtensionForFormat`/`PayloadFormat` seeds/`StubRegistry`/`_legacyParser`), `LrcLibLyricsProviderTests`, `LyricifyLyricsProviderTests`, the `FormatTag_Is*` parser tests, `LyricsParserRegistryTests` (`FakeParser` + `Parse_UnregisteredFormat` redesign), and `LyricsProviderRegistrationTests` (moved into Task 3 — it referenced the deleted `NeteaseLrcLyricsParser`). Task 5 adds `FakeLyricsProvider.DisplayName` and constructs providers directly (the assumed `ResolveProviders()` helper did not exist and wired only the CN trio). Task 7 uses the real `Build(...)` overloads (not the non-existent `CreateViewModel()`). Task 4's migration test uses the real fixture (`_cacheRoot`/`CreateStore()`). The `LyricsProviderResult` ctor placeholder was replaced with real code.
