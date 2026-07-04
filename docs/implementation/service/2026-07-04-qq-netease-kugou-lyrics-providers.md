# QQ / Netease / Kugou Lyrics Providers — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three word/line-tier external lyric sources — QQ Music, Netease, Kugou — as `ILyricsProvider`s that join the existing fan-out, fetching via the `Lyricify.Lyrics.Helper` NuGet and gating fuzzy matches with a shared confidence scorer.

**Architecture:** Three thin `ILyricsProvider`s over one `LyricifyLyricsClient` facade (the single seam touching the library). Each provider searches → scores the candidate with a shared `LyricsMatchScorer` → rejects below threshold → parses the raw QRC/KRC/YRC/LRC via the shared parser registry. No changes to `LyricsService`, `LyricsFallbackPolicy`, the cache schema, the data model, or the UI beyond additive registration and two cache-extension maps. lrclib's `/api/search` is unified onto the same scorer.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.19041.0`), WinUI 3, `Lyricify.Lyrics.Helper` 0.1.4 (Apache-2.0), MSTest, `Microsoft.Extensions.DependencyInjection`/`Options`.

**Design spec:** `docs/design/service/2026-07-04-qq-netease-kugou-lyrics-providers.md`.

## Global Constraints

- **Platform build flag:** always build/test the app + `Hypersonic.Tests` with `-p:Platform=x64` (e.g. `dotnet build Hypersonic.sln -p:Platform=x64`). `Hypersonic.Core.Tests` runs with plain `dotnet test`.
- **Central package management:** package versions live in `Directory.Packages.props` (`<PackageVersion>`); project files use versionless `<PackageReference>`.
- **Core stays dependency-free:** `Hypersonic.Core` must not reference `Lyricify.Lyrics.Helper` or any NuGet. The library is referenced only by `Hypersonic`.
- **`QQ` casing:** the `QQ` brand is double-capital in every type and display name (`QQMusicLyricsProvider`, `QQLyricsOptions`, `QQLyricsConstants`). Persisted provider ids are all-lowercase (`"qq"`, `"netease"`, `"kugou"`), matching `"lrclib"`/`"amll-ttml-db"`.
- **Provider outcomes:** caller-token `OperationCanceledException` is always re-thrown (never masked); library/network errors → `Transient`; a present-but-unparseable payload → `Transient`; no candidate / below-threshold / disabled → `DefinitiveEmpty`.
- **Parsers** always run `LyricsValidator.Validate` and return `null` (logging once) when the document has issues.
- **Code style:** 4-space indent, Allman braces, file-scoped namespaces, `var`, CRLF line endings.
- **Commits:** Conventional Commits with scope, e.g. `feat(lyrics): ...`, `refactor(lyrics): ...`, `test(lyrics): ...`.
- **Logging:** obtain a category logger via `ILoggingService.GetLogger(nameof(Type))`; `Warning(message, context?, exception?)`.

---

### Task 1: Add the `Lyricify.Lyrics.Helper` dependency

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `Hypersonic/Hypersonic.csproj:37-58` (the app's `PackageReference` `ItemGroup`)

**Interfaces:**
- Consumes: nothing.
- Produces: the `Lyricify.Lyrics.*` namespaces available to the `Hypersonic` project (`Lyricify.Lyrics.Helpers`, `.Parsers`, `.Models`, `.Searchers`, `.Decrypter.Krc`).

- [ ] **Step 1: Add the central package version**

In `Directory.Packages.props`, add inside the `<ItemGroup>` (keep the list alphabetical — place after the `H.NotifyIcon.WinUI` line):

```xml
    <PackageVersion Include="Lyricify.Lyrics.Helper" Version="0.1.4" />
```

- [ ] **Step 2: Reference it from the app project**

In `Hypersonic/Hypersonic.csproj`, add to the third `<ItemGroup>` (the one with the other `PackageReference`s), after the `H.NotifyIcon.WinUI` line:

```xml
    <PackageReference Include="Lyricify.Lyrics.Helper" />
```

- [ ] **Step 3: Restore + build to verify TFM compatibility and the transitive Newtonsoft.Json dep**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: PASS. The package restores and the app still compiles. (The library pulls in `Newtonsoft.Json` transitively — this is expected; no code change needed.)

If restore fails on target framework, stop and report — the library targets .NET Standard 2.0 and should be compatible with `net10.0-windows`, but a mismatch must be resolved before continuing.

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props Hypersonic/Hypersonic.csproj
git commit -m "build(lyrics): add Lyricify.Lyrics.Helper dependency"
```

---

### Task 2: `LyricsMatchScorer` + `LyricsCandidate` (Core, pure)

**Files:**
- Create: `Hypersonic.Core/Models/Lyrics/LyricsCandidate.cs`
- Create: `Hypersonic.Core/Services/Lyrics/LyricsMatchScorer.cs`
- Test: `Hypersonic.Core.Tests/Services/Lyrics/LyricsMatchScorerTests.cs`

**Interfaces:**
- Consumes: `LyricsQuery` (`Hypersonic.Core.Models.Lyrics`, fields `Title`, `Artist`, `Album`, `DurationSeconds`).
- Produces:
  - `record LyricsCandidate(string? Title, string? Artist, string? Album, int? DurationSeconds)` in `Hypersonic.Core.Models.Lyrics`.
  - `static int LyricsMatchScorer.Score(LyricsQuery query, LyricsCandidate candidate)` in `Hypersonic.Core.Services.Lyrics` — returns 0–100.

- [ ] **Step 1: Write the failing tests**

Create `Hypersonic.Core.Tests/Services/Lyrics/LyricsMatchScorerTests.cs`:

```csharp
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class LyricsMatchScorerTests
{
    private static LyricsQuery Query(string title = "Blinding Lights", string? artist = "The Weeknd",
        string? album = "After Hours", int? dur = 200)
        => new("song-1", Artist: artist, Title: title, Album: album, DurationSeconds: dur);

    [TestMethod]
    public void ExactMatch_ScoresHigh()
    {
        var score = LyricsMatchScorer.Score(Query(),
            new LyricsCandidate("Blinding Lights", "The Weeknd", "After Hours", 200));
        Assert.IsTrue(score >= 95, $"expected near-perfect, got {score}");
    }

    [TestMethod]
    public void WrongSong_ScoresLow()
    {
        var score = LyricsMatchScorer.Score(Query(),
            new LyricsCandidate("Bohemian Rhapsody", "Queen", "A Night at the Opera", 355));
        Assert.IsTrue(score <= 25, $"expected low, got {score}");
    }

    [TestMethod]
    public void ExtraCandidateArtist_IsIgnored()
    {
        // Query artist is a subset of the candidate's — should not be penalized.
        var score = LyricsMatchScorer.Score(Query(artist: "The Weeknd"),
            new LyricsCandidate("Blinding Lights", "The Weeknd/ROSALÍA", "After Hours", 200));
        Assert.IsTrue(score >= 95, $"extra artist should not hurt, got {score}");
    }

    [TestMethod]
    public void MissingQueriedArtist_IsPenalized()
    {
        // Query has two artists; candidate only one. best(A)=1, best(B)~0 → artist factor ~0.5.
        var full = LyricsMatchScorer.Score(Query(artist: "A/B"),
            new LyricsCandidate("Blinding Lights", "A/B", "After Hours", 200));
        var half = LyricsMatchScorer.Score(Query(artist: "A/B"),
            new LyricsCandidate("Blinding Lights", "A", "After Hours", 200));
        Assert.IsTrue(half < full, $"missing collaborator should lower score ({half} !< {full})");
    }

    [TestMethod]
    public void MissingDuration_IsNeutral_NotPenalized()
    {
        var withDur = LyricsMatchScorer.Score(Query(dur: 200),
            new LyricsCandidate("Blinding Lights", "The Weeknd", "After Hours", 260)); // 60s off
        var noDur = LyricsMatchScorer.Score(Query(dur: null),
            new LyricsCandidate("Blinding Lights", "The Weeknd", "After Hours", null));
        Assert.IsTrue(noDur > withDur, "absent duration must not penalize like a mismatch does");
    }

    [TestMethod]
    public void CaseAndWidth_AreNormalized()
    {
        var score = LyricsMatchScorer.Score(Query(title: "blinding lights"),
            new LyricsCandidate("ＢＬＩＮＤＩＮＧ　ＬＩＧＨＴＳ", "The Weeknd", "After Hours", 200));
        Assert.IsTrue(score >= 95, $"full-width + case should normalize, got {score}");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsMatchScorerTests"`
Expected: FAIL to compile — `LyricsCandidate` / `LyricsMatchScorer` don't exist.

- [ ] **Step 3: Create `LyricsCandidate`**

Create `Hypersonic.Core/Models/Lyrics/LyricsCandidate.cs`:

```csharp
namespace Hypersonic.Core.Models.Lyrics;

/// <summary>
/// A search result's identifying metadata, scored against a <see cref="LyricsQuery"/> by
/// <see cref="Hypersonic.Core.Services.Lyrics.LyricsMatchScorer"/>. Provider/library-agnostic so
/// the scorer stays pure and Core-testable.
/// </summary>
public sealed record LyricsCandidate(string? Title, string? Artist, string? Album, int? DurationSeconds);
```

- [ ] **Step 4: Implement `LyricsMatchScorer`**

Create `Hypersonic.Core/Services/Lyrics/LyricsMatchScorer.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Pure, weighted match scorer for a search candidate against a query. Combines normalized-Levenshtein
/// title/album/artist similarity with duration proximity into a 0–100 score. A factor whose data is
/// absent on either side is dropped and the remaining weights renormalized (missing = neutral). Used
/// for both candidate selection (max score) and confidence gating (score ≥ threshold).
/// </summary>
public static class LyricsMatchScorer
{
    private const double TitleWeight = 0.35;
    private const double ArtistWeight = 0.30;
    private const double DurationWeight = 0.30;
    private const double AlbumWeight = 0.05;
    private const int DurationToleranceSeconds = 12;

    private static readonly char[] ArtistSeparators = ['/', ',', ';', '&', '、', '，'];
    private static readonly string[] ArtistWordDelimiters =
        [" feat.", " feat ", " ft.", " ft ", " featuring ", " vs.", " vs "];

    public static int Score(LyricsQuery query, LyricsCandidate candidate)
    {
        double acc = 0;
        double weightSum = 0;

        // Title — always present (blank query title is rejected upstream).
        AddFactor(ref acc, ref weightSum, TitleWeight,
            Similarity(StripQualifier(Normalize(query.Title)), StripQualifier(Normalize(candidate.Title))));

        if (!string.IsNullOrWhiteSpace(query.Album) && !string.IsNullOrWhiteSpace(candidate.Album))
        {
            AddFactor(ref acc, ref weightSum, AlbumWeight,
                Similarity(StripQualifier(Normalize(query.Album)), StripQualifier(Normalize(candidate.Album))));
        }

        if (query.DurationSeconds is int qd && candidate.DurationSeconds is int cd)
        {
            var delta = Math.Abs(cd - qd);
            var sim = Math.Max(0.0, 1.0 - (double)delta / DurationToleranceSeconds);
            AddFactor(ref acc, ref weightSum, DurationWeight, sim);
        }

        var queryTokens = ArtistTokens(query.Artist);
        var candTokens = ArtistTokens(candidate.Artist);
        if (queryTokens.Count > 0 && candTokens.Count > 0)
        {
            double sum = 0;
            foreach (var q in queryTokens)
            {
                double best = 0;
                foreach (var c in candTokens)
                {
                    best = Math.Max(best, Similarity(q, c));
                }
                sum += best;
            }
            AddFactor(ref acc, ref weightSum, ArtistWeight, sum / queryTokens.Count);
        }

        return weightSum <= 0 ? 0 : (int)Math.Round(100.0 * acc / weightSum);
    }

    private static void AddFactor(ref double acc, ref double weightSum, double weight, double factor)
    {
        acc += weight * factor;
        weightSum += weight;
    }

    internal static double Similarity(string a, string b)
    {
        var max = Math.Max(a.Length, b.Length);
        return max == 0 ? 1.0 : 1.0 - (double)Levenshtein(a, b) / max;
    }

    internal static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var collapsed = Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
        var sb = new StringBuilder(collapsed.Length);
        foreach (var ch in collapsed)
        {
            if (ch >= '！' && ch <= '～') sb.Append((char)(ch - 0xFEE0)); // full-width → half-width
            else if (ch == '　') sb.Append(' ');                              // ideographic space
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string StripQualifier(string normalized)
        => Regex.Replace(normalized, @"\s*[\(\[].*?[\)\]]\s*$", string.Empty).Trim();

    internal static List<string> ArtistTokens(string? artist)
    {
        var normalized = Normalize(artist);
        if (normalized.Length == 0) return [];

        foreach (var delimiter in ArtistWordDelimiters)
        {
            normalized = normalized.Replace(delimiter, "/", StringComparison.Ordinal);
        }

        return normalized
            .Split(ArtistSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~LyricsMatchScorerTests"`
Expected: PASS (all 6).

- [ ] **Step 6: Commit**

```bash
git add Hypersonic.Core/Models/Lyrics/LyricsCandidate.cs Hypersonic.Core/Services/Lyrics/LyricsMatchScorer.cs Hypersonic.Core.Tests/Services/Lyrics/LyricsMatchScorerTests.cs
git commit -m "feat(lyrics): add LyricsMatchScorer + LyricsCandidate"
```

---

### Task 3: CN source constants (Core)

**Files:**
- Create: `Hypersonic.Core/Services/Lyrics/QQLyricsConstants.cs`
- Create: `Hypersonic.Core/Services/Lyrics/KugouLyricsConstants.cs`
- Create: `Hypersonic.Core/Services/Lyrics/NeteaseLyricsConstants.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: provider ids and payload format tags referenced by the word parsers (Task 4), the LRC parser (Task 5), the facade (Task 6), the providers (Task 7), and the cache maps (Task 8):
  - `QQLyricsConstants.ProviderId = "qq"`, `QQLyricsConstants.QrcFormat = "qrc"`
  - `KugouLyricsConstants.ProviderId = "kugou"`, `KugouLyricsConstants.KrcFormat = "krc"`
  - `NeteaseLyricsConstants.ProviderId = "netease"`, `NeteaseLyricsConstants.YrcFormat = "yrc"`, `NeteaseLyricsConstants.LrcFormat = "netease-lrc"`

These live in Core (like `LrcLibLyricsConstants` / `TtmlLyricsConstants`) so both the parsers (Core/Hypersonic) and the providers/service (Hypersonic) share one owner. No test — pure constants; verified by compilation in the tasks that consume them.

- [ ] **Step 1: Create the three constants files**

`Hypersonic.Core/Services/Lyrics/QQLyricsConstants.cs`:

```csharp
namespace Hypersonic.Core.Services.Lyrics;

/// <summary>Constants for the QQ Music lyrics source: its provider id and QRC payload format tag.</summary>
public static class QQLyricsConstants
{
    public const string ProviderId = "qq";
    public const string QrcFormat = "qrc";
}
```

`Hypersonic.Core/Services/Lyrics/KugouLyricsConstants.cs`:

```csharp
namespace Hypersonic.Core.Services.Lyrics;

/// <summary>Constants for the Kugou lyrics source: its provider id and KRC payload format tag.</summary>
public static class KugouLyricsConstants
{
    public const string ProviderId = "kugou";
    public const string KrcFormat = "krc";
}
```

`Hypersonic.Core/Services/Lyrics/NeteaseLyricsConstants.cs`:

```csharp
namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Constants for the Netease lyrics source: its provider id, the YRC (word) format tag, and a
/// source-scoped LRC (line) format tag distinct from lrclib's so the parser stamps "netease".
/// </summary>
public static class NeteaseLyricsConstants
{
    public const string ProviderId = "netease";
    public const string YrcFormat = "yrc";
    public const string LrcFormat = "netease-lrc";
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build Hypersonic.Core/Hypersonic.Core.csproj`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Hypersonic.Core/Services/Lyrics/QQLyricsConstants.cs Hypersonic.Core/Services/Lyrics/KugouLyricsConstants.cs Hypersonic.Core/Services/Lyrics/NeteaseLyricsConstants.cs
git commit -m "feat(lyrics): add QQ/Netease/Kugou source constants"
```

---

### Task 4: Word parsers + syllable mapper (Hypersonic)

**Files:**
- Create: `Hypersonic/Services/Lyrics/LyricifySyllableMapper.cs`
- Create: `Hypersonic/Services/Lyrics/LyricifyWordLyricsParser.cs`
- Test: `Hypersonic.Tests/Services/Lyrics/LyricifyWordLyricsParserTests.cs`

**Interfaces:**
- Consumes: `Lyricify.Lyrics.Helpers.ParseHelper.ParseLyrics(string, Lyricify.Lyrics.Models.LyricsRawTypes)` → `Lyricify.Lyrics.Models.LyricsData?` with `.Lines` (`List<ILineInfo>`); `Lyricify.Lyrics.Models.SyllableLineInfo` (`.Syllables`, each with `.StartTime`, `.EndTime`, `.Text`); `ILyricsPayloadParser`, `LyricsValidator`, `LyricsDocument`/`LyricsLine`/`LyricsWord` (Core); the Task 3 constants; `ILoggingService`.
- Produces:
  - `internal static LyricsDocument? LyricifySyllableMapper.ToWordDocument(IReadOnlyList<Lyricify.Lyrics.Models.ILineInfo> lines, string sourceId)`.
  - `internal sealed class LyricifyWordLyricsParser : ILyricsPayloadParser` with ctor `(string formatTag, string sourceId, Lyricify.Lyrics.Models.LyricsRawTypes rawType, ILoggingService logService)`.

> **Library note:** syllables are `(line as SyllableLineInfo)?.Syllables`, each a `SyllableInfo` with **`int`** `StartTime` / `EndTime` (milliseconds) and a `string Text` — verified in the library source, so no cast is needed. `ILineInfo.StartTime` is `int?`.

- [ ] **Step 1: Write the failing tests**

Create `Hypersonic.Tests/Services/Lyrics/LyricifyWordLyricsParserTests.cs`:

```csharp
using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Services.Lyrics;

using Lyricify.Lyrics.Models;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricifyWordLyricsParserTests
{
    // Minimal QRC: [lineStart,lineDuration]word(wordStart,wordDuration)...
    // If this literal does not parse under the library, replace it with a captured sample from the
    // library's Lyricify.Lyrics.Demo/RawLyrics/QrcDemo.txt — the test's red→green loop is the check.
    private const string QrcSample =
        "[0,1000]Hello(0,500)World(500,500)\n[1000,1000]Second(1000,500)Line(1500,500)";

    private static LyricifyWordLyricsParser QrcParser()
        => new(QQLyricsConstants.QrcFormat, QQLyricsConstants.ProviderId, LyricsRawTypes.Qrc, new LoggingService());

    [TestMethod]
    public void FormatTag_IsQrc()
        => Assert.AreEqual("qrc", QrcParser().FormatTag);

    [TestMethod]
    public void Qrc_ParsesToWordDocument()
    {
        var doc = QrcParser().Parse(Encoding.UTF8.GetBytes(QrcSample));

        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Word, doc!.SyncKind);
        Assert.AreEqual("qq", doc.SourceProvider);
        Assert.AreEqual(2, doc.Lines.Count);
        Assert.IsTrue(doc.Lines[0].Words.Count >= 2, "first line should carry word timings");
        // Validator invariants hold (word bounds within line, monotonic) — a parsed doc is already validated.
        Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
    }

    [TestMethod]
    public void Garbage_ReturnsNull()
        => Assert.IsNull(QrcParser().Parse(Encoding.UTF8.GetBytes("not a lyric at all")));

    [TestMethod]
    public void SourceId_FlowsThroughRegardlessOfFormat()
    {
        // Proves the sourceId parameter is stamped onto the document, so the Netease/Kugou instances
        // (registered in Task 9) will stamp "netease"/"kugou" — without needing per-format sample text here.
        var parser = new LyricifyWordLyricsParser(
            QQLyricsConstants.QrcFormat, NeteaseLyricsConstants.ProviderId, LyricsRawTypes.Qrc, new LoggingService());
        var doc = parser.Parse(Encoding.UTF8.GetBytes(QrcSample));
        Assert.AreEqual("netease", doc!.SourceProvider);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricifyWordLyricsParserTests"`
Expected: FAIL to compile — the mapper/parser don't exist.

- [ ] **Step 3: Implement the syllable mapper**

Create `Hypersonic/Services/Lyrics/LyricifySyllableMapper.cs`:

```csharp
using Hypersonic.Core.Models.Lyrics;

using Lyricify.Lyrics.Models;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Maps the library's parsed line/syllable model (<see cref="ILineInfo"/> / <see cref="SyllableLineInfo"/>)
/// into a word-tier <see cref="LyricsDocument"/>. Mirrors <c>TtmlLyricsParser</c>: normalizes word
/// timings (monotonic starts, end ≥ start) and sets each line's bounds to enclose all its words so the
/// validator's word-in-bounds invariant holds. A line with no syllables gets one synthetic word.
/// </summary>
internal static class LyricifySyllableMapper
{
    public static LyricsDocument? ToWordDocument(IReadOnlyList<ILineInfo> lines, string sourceId)
    {
        var outLines = new List<LyricsLine>(lines.Count);

        foreach (var line in lines)
        {
            var text = line.Text ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            var words = new List<LyricsWord>();
            if (line is SyllableLineInfo syllabic && syllabic.Syllables is { Count: > 0 } syllables)
            {
                foreach (var s in syllables)
                {
                    // SyllableInfo.StartTime/EndTime are int ms in the library — no cast needed.
                    words.Add(new LyricsWord { StartMs = s.StartTime, EndMs = Math.Max(s.EndTime, s.StartTime), Text = s.Text ?? string.Empty });
                }
            }
            else
            {
                var start = line.StartTime ?? 0;
                words.Add(new LyricsWord { StartMs = start, EndMs = start, Text = text });
            }

            words = NormalizeWordTimings(words);

            var minStart = int.MaxValue;
            var maxEnd = int.MinValue;
            foreach (var word in words)
            {
                minStart = Math.Min(minStart, word.StartMs);
                maxEnd = Math.Max(maxEnd, word.EndMs);
            }

            outLines.Add(new LyricsLine
            {
                StartMs = Math.Max(minStart, 0),
                EndMs = maxEnd,
                Text = text,
                Words = words,
            });
        }

        if (outLines.Count == 0)
        {
            return null;
        }

        return new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            Lines = outLines,
            SourceProvider = sourceId,
        };
    }

    private static List<LyricsWord> NormalizeWordTimings(List<LyricsWord> words)
    {
        var normalized = new List<LyricsWord>(words.Count);
        var previousStart = int.MinValue;

        foreach (var word in words)
        {
            var start = Math.Max(word.StartMs, previousStart);
            var end = Math.Max(word.EndMs, start);
            normalized.Add(new LyricsWord { StartMs = start, EndMs = end, Text = word.Text });
            previousStart = start;
        }

        return normalized;
    }
}
```

- [ ] **Step 4: Implement the parametrized word parser**

Create `Hypersonic/Services/Lyrics/LyricifyWordLyricsParser.cs`:

```csharp
using System.Text;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;

using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// A word-tier <see cref="ILyricsPayloadParser"/> for a library-parseable syllable format (QRC / KRC /
/// YRC). Parametrized by format tag, the stamped source id, and the library raw type so one class
/// serves all three (registered three times in DI). Parses the raw text via the library, maps through
/// <see cref="LyricifySyllableMapper"/>, validates, and discards an invalid document.
/// </summary>
internal sealed class LyricifyWordLyricsParser : ILyricsPayloadParser
{
    private readonly string _formatTag;
    private readonly string _sourceId;
    private readonly LyricsRawTypes _rawType;
    private readonly ILogger _log;

    public LyricifyWordLyricsParser(string formatTag, string sourceId, LyricsRawTypes rawType, ILoggingService logService)
    {
        _formatTag = formatTag;
        _sourceId = sourceId;
        _rawType = rawType;
        _log = logService.GetLogger(nameof(LyricifyWordLyricsParser));
    }

    public string FormatTag => _formatTag;

    public LyricsDocument? Parse(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return null;
        }

        List<ILineInfo>? lines;
        try
        {
            lines = ParseHelper.ParseLyrics(Encoding.UTF8.GetString(payload.Span), _rawType)?.Lines;
        }
        catch (Exception ex)
        {
            _log.Warning("Lyricify parse threw; discarding", new { _formatTag }, ex);
            return null;
        }

        if (lines is null || lines.Count == 0)
        {
            return null;
        }

        var document = LyricifySyllableMapper.ToWordDocument(lines, _sourceId);
        if (document is null)
        {
            return null;
        }

        var issues = LyricsValidator.Validate(document);
        if (issues.Count > 0)
        {
            _log.Warning($"{_formatTag} document failed validation; discarding",
                new { IssueCount = issues.Count, FirstIssue = issues[0] });
            return null;
        }

        return document;
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricifyWordLyricsParserTests"`
Expected: PASS. If `Qrc_ParsesToWordDocument` fails because `QrcSample` isn't valid QRC, replace the literal with a captured sample from the library's `RawLyrics/QrcDemo.txt` and re-run — the mapping assertions stay the same.

- [ ] **Step 6: Commit**

```bash
git add Hypersonic/Services/Lyrics/LyricifySyllableMapper.cs Hypersonic/Services/Lyrics/LyricifyWordLyricsParser.cs Hypersonic.Tests/Services/Lyrics/LyricifyWordLyricsParserTests.cs
git commit -m "feat(lyrics): add Lyricify word parser + syllable mapper"
```

---

### Task 5: `NeteaseLrcLyricsParser` (Core)

**Files:**
- Create: `Hypersonic.Core/Services/Lyrics/NeteaseLrcLyricsParser.cs`
- Test: `Hypersonic.Core.Tests/Services/Lyrics/NeteaseLrcLyricsParserTests.cs`

**Interfaces:**
- Consumes: `LrcLineParser.Parse(string)` → `LrcParseResult` (`.Lines` of `LrcTimedLine(int TimeMs, string Text)`, `.OffsetMs`); `NeteaseLyricsConstants`; `ILyricsPayloadParser`, `LyricsValidator`, `ILoggingService`.
- Produces: `public sealed class NeteaseLrcLyricsParser : ILyricsPayloadParser` with `FormatTag => "netease-lrc"`, stamping `SourceProvider = "netease"`, `SyncKind = Line`.

This mirrors `LrcSyncedLyricsParser` exactly, differing only in the format tag and the stamped source. It is the line-tier fallback for Netease when the response carries no YRC.

- [ ] **Step 1: Write the failing test**

Create `Hypersonic.Core.Tests/Services/Lyrics/NeteaseLrcLyricsParserTests.cs`:

```csharp
using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class NeteaseLrcLyricsParserTests
{
    private static NeteaseLrcLyricsParser Parser() => new(new LoggingService());

    [TestMethod]
    public void FormatTag_IsNeteaseLrc() => Assert.AreEqual("netease-lrc", Parser().FormatTag);

    [TestMethod]
    public void Lrc_ParsesToLineDocument_StampedNetease()
    {
        var doc = Parser().Parse(Encoding.UTF8.GetBytes("[00:01.00]first\n[00:03.50]second"));

        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Line, doc!.SyncKind);
        Assert.AreEqual("netease", doc.SourceProvider);
        Assert.AreEqual(2, doc.Lines.Count);
        Assert.AreEqual(1000, doc.Lines[0].StartMs);
    }

    [TestMethod]
    public void Untimed_ReturnsNull()
        => Assert.IsNull(Parser().Parse(Encoding.UTF8.GetBytes("no timestamps here")));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~NeteaseLrcLyricsParserTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement (adapting `LrcSyncedLyricsParser`)**

Create `Hypersonic.Core/Services/Lyrics/NeteaseLrcLyricsParser.cs`:

```csharp
using System.Text;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Parses Netease's line-level LRC payload into a <see cref="LyricsSyncKind.Line"/> document via
/// <see cref="LrcLineParser"/>. Identical in shape to <c>LrcSyncedLyricsParser</c> but stamps the
/// <c>netease</c> source and the <c>netease-lrc</c> format tag, so a Netease line result is cached and
/// attributed distinctly from an lrclib one.
/// </summary>
public sealed class NeteaseLrcLyricsParser : ILyricsPayloadParser
{
    private readonly ILogger _log;

    public NeteaseLrcLyricsParser(ILoggingService logService)
        => _log = logService.GetLogger(nameof(NeteaseLrcLyricsParser));

    public string FormatTag => NeteaseLyricsConstants.LrcFormat;

    public LyricsDocument? Parse(ReadOnlyMemory<byte> payload)
    {
        var parsed = LrcLineParser.Parse(Encoding.UTF8.GetString(payload.Span));
        if (parsed.Lines.Count == 0)
        {
            return null;
        }

        var lines = new List<LyricsLine>(parsed.Lines.Count);
        foreach (var line in parsed.Lines)
        {
            lines.Add(new LyricsLine { StartMs = line.TimeMs, Text = line.Text });
        }

        var document = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Lines = lines,
            SourceProvider = NeteaseLyricsConstants.ProviderId,
            LanguageCode = null,
            OffsetMs = parsed.OffsetMs,
        };

        var issues = LyricsValidator.Validate(document);
        if (issues.Count > 0)
        {
            _log.Warning("Netease LRC document failed validation; discarding",
                new { IssueCount = issues.Count, FirstIssue = issues[0] });
            return null;
        }

        return document;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~NeteaseLrcLyricsParserTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hypersonic.Core/Services/Lyrics/NeteaseLrcLyricsParser.cs Hypersonic.Core.Tests/Services/Lyrics/NeteaseLrcLyricsParserTests.cs
git commit -m "feat(lyrics): add Netease LRC (line) parser"
```

---

### Task 6: `LyricifyLyricsClient` facade

**Files:**
- Create: `Hypersonic/Services/Lyrics/ILyricifyLyricsClient.cs`
- Create: `Hypersonic/Services/Lyrics/LyricifyLyricsClient.cs`

**Interfaces:**
- Consumes: `Lyricify.Lyrics.Helpers.SearchHelper.Search(TrackMultiArtistMetadata, Searchers, CompareHelper.MatchType)`; `Lyricify.Lyrics.Helpers.ProviderHelper.{QQMusicApi.GetLyricsAsync(id), NeteaseApi.GetLyricNew(id), KugouApi.GetSearchLyrics(hash:)}` (use `GetLyricNew`, the v1/YRC-capable endpoint — **not** `GetLyric`, which is LRC-only); `Lyricify.Lyrics.Decrypter.Krc.Helper.GetLyricsAsync(id, accessKey)`; search-result subtypes `QQMusicSearchResult(.Id)`, `NeteaseSearchResult(.Id)`, `KugouSearchResult(.Hash)`; Task 3 constants; `LyricsQuery`, `LyricsCandidate` (Core); `ILoggingService`.
- Produces:
  - `enum LyricifyPlatform { QQMusic, Netease, Kugou }`
  - `sealed record LyricifyLyricsResult(string RawText, string FormatTag, LyricsCandidate Candidate)`
  - `interface ILyricifyLyricsClient { Task<LyricifyLyricsResult?> FetchAsync(LyricifyPlatform platform, LyricsQuery query, CancellationToken cancellationToken); }` — returns `null` when the search yields no candidate; **throws** on library/network errors (the provider maps that to `Transient`).

> This type performs live network I/O against reverse-engineered endpoints and cannot be unit-tested deterministically. It is intentionally thin; correctness of the *provider* logic is covered in Task 7 against a fake `ILyricifyLyricsClient`. Verify this task by build + the manual smoke check in Step 4.

> **Library note:** exact member names are taken from BetterLyrics' usage (`SearchQQNeteaseKugouAsync` in `../BetterLyrics/src/BetterLyrics.WinUI3/Services/LyricsSearchService.cs`). If a member differs in 0.1.4 (e.g. a response property name), adjust against IntelliSense — the shape (search → typed result → per-provider fetch/decrypt → raw string) is stable.

- [ ] **Step 1: Create the interface + supporting types**

Create `Hypersonic/Services/Lyrics/ILyricifyLyricsClient.cs`:

```csharp
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>Which external platform to search/fetch through the Lyricify library.</summary>
public enum LyricifyPlatform
{
    QQMusic,
    Netease,
    Kugou,
}

/// <summary>The library's best raw answer for a query: decrypted lyric text, its payload format tag,
/// and the matched candidate's metadata (for the provider's confidence gate).</summary>
public sealed record LyricifyLyricsResult(string RawText, string FormatTag, LyricsCandidate Candidate);

/// <summary>
/// The single seam over <c>Lyricify.Lyrics.Helper</c>: search + fetch + decrypt for one platform.
/// Returns <c>null</c> when the search yields no candidate; throws on library/network failure (callers
/// map that to a transient outcome). Isolating the library here keeps the providers testable and bounds
/// the blast radius of a future library swap.
/// </summary>
public interface ILyricifyLyricsClient
{
    Task<LyricifyLyricsResult?> FetchAsync(LyricifyPlatform platform, LyricsQuery query, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implement the facade**

Create `Hypersonic/Services/Lyrics/LyricifyLyricsClient.cs`:

```csharp
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Searchers;

namespace Hypersonic.Services.Lyrics;

/// <summary>Concrete <see cref="ILyricifyLyricsClient"/> over the Lyricify library.</summary>
internal sealed class LyricifyLyricsClient : ILyricifyLyricsClient
{
    private readonly ILogger _log;

    public LyricifyLyricsClient(ILoggingService logService)
        => _log = logService.GetLogger(nameof(LyricifyLyricsClient));

    public async Task<LyricifyLyricsResult?> FetchAsync(LyricifyPlatform platform, LyricsQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var searcher = platform switch
        {
            LyricifyPlatform.QQMusic => Searchers.QQMusic,
            LyricifyPlatform.Netease => Searchers.Netease,
            LyricifyPlatform.Kugou => Searchers.Kugou,
            _ => Searchers.QQMusic,
        };

        var metadata = new TrackMultiArtistMetadata
        {
            Title = query.Title,
            Artist = query.Artist,
            Album = query.Album,
            DurationMs = (query.DurationSeconds ?? 0) * 1000,
        };

        var result = await SearchHelper.Search(metadata, searcher,
            Lyricify.Lyrics.Searchers.Helpers.CompareHelper.MatchType.NoMatch).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is null)
        {
            return null;
        }

        var candidate = new LyricsCandidate(
            result.Title, result.Artist, result.Album,
            result.DurationMs > 0 ? result.DurationMs / 1000 : null);

        var (rawText, formatTag) = await FetchRawAsync(platform, result, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        return new LyricifyLyricsResult(rawText!, formatTag, candidate);
    }

    private static async Task<(string? Raw, string FormatTag)> FetchRawAsync(
        LyricifyPlatform platform, ISearchResult result, CancellationToken cancellationToken)
    {
        switch (platform)
        {
            case LyricifyPlatform.QQMusic when result is QQMusicSearchResult qq:
            {
                var response = await ProviderHelper.QQMusicApi.GetLyricsAsync(qq.Id).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return (response?.Lyrics, QQLyricsConstants.QrcFormat);
            }
            case LyricifyPlatform.Netease when result is NeteaseSearchResult ne:
            {
                // GetLyricNew hits the eapi/song/lyric/v1 endpoint, which returns word-level YRC (yv/ytv/yrv).
                // The older GetLyric hits weapi/song/lyric and returns LRC only — it never yields YRC, so it
                // must NOT be used here or Netease silently degrades to line-tier.
                var response = await ProviderHelper.NeteaseApi.GetLyricNew(ne.Id).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var yrc = response?.Yrc?.Lyric;
                return !string.IsNullOrWhiteSpace(yrc)
                    ? (yrc, NeteaseLyricsConstants.YrcFormat)
                    : (response?.Lrc?.Lyric, NeteaseLyricsConstants.LrcFormat);
            }
            case LyricifyPlatform.Kugou when result is KugouSearchResult kg:
            {
                var response = await ProviderHelper.KugouApi.GetSearchLyrics(hash: kg.Hash).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = response?.Candidates?.FirstOrDefault();
                if (candidate is null)
                {
                    return (null, KugouLyricsConstants.KrcFormat);
                }

                var krc = await Lyricify.Lyrics.Decrypter.Krc.Helper
                    .GetLyricsAsync(candidate.Id, candidate.AccessKey).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return (krc, KugouLyricsConstants.KrcFormat);
            }
            default:
                return (null, string.Empty);
        }
    }
}
```

- [ ] **Step 3: Build to verify the library surface compiles**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: PASS. If a member name (e.g. `response.Lyrics`, `Yrc.Lyric`, `Candidates`, `AccessKey`) doesn't resolve, correct it against IntelliSense and rebuild.

- [ ] **Step 4: Manual smoke check (deferred until after Task 9 DI wiring)**

There is no unit test for this class. After Task 9 registers everything, the manual verification is: run the app, play a track known to exist on QQ/Netease, click the lyrics **Refresh** button, and confirm word/line lyrics appear (or that failures degrade silently to "no lyrics"). Note this as a follow-up check; do not block the commit on it.

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/Services/Lyrics/ILyricifyLyricsClient.cs Hypersonic/Services/Lyrics/LyricifyLyricsClient.cs
git commit -m "feat(lyrics): add LyricifyLyricsClient facade over the NuGet"
```

---

### Task 7: The three providers + options (Hypersonic)

**Files:**
- Create: `Hypersonic/Models/ExternalSearchLyricsOptions.cs` (base + `QQLyricsOptions` / `NeteaseLyricsOptions` / `KugouLyricsOptions`)
- Create: `Hypersonic/Services/Lyrics/CnLyricsProviderBase.cs`
- Create: `Hypersonic/Services/Lyrics/QQMusicLyricsProvider.cs`
- Create: `Hypersonic/Services/Lyrics/NeteaseLyricsProvider.cs`
- Create: `Hypersonic/Services/Lyrics/KugouLyricsProvider.cs`
- Test: `Hypersonic.Tests/Services/Lyrics/CnLyricsProviderTests.cs`

**Interfaces:**
- Consumes: `ILyricifyLyricsClient` (Task 6), `ILyricsParserRegistry`, `LyricsMatchScorer.Score` (Task 2), `ILyricsProvider`/`LyricsProviderResult`/`LyricsQuery` (Core), Task 3 constants, `IOptions<T>`, `ILoggingService`.
- Produces: `QQMusicLyricsProvider`, `NeteaseLyricsProvider`, `KugouLyricsProvider` (all `ILyricsProvider`), and the three options types (with `Enabled` default `true`, `MatchThreshold` default `55`).

- [ ] **Step 1: Write the failing tests**

Create `Hypersonic.Tests/Services/Lyrics/CnLyricsProviderTests.cs`:

```csharp
using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services.Lyrics;

using Lyricify.Lyrics.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class CnLyricsProviderTests
{
    private sealed class FakeClient : ILyricifyLyricsClient
    {
        public LyricifyLyricsResult? Result;
        public Exception? Throw;
        public Task<LyricifyLyricsResult?> FetchAsync(LyricifyPlatform platform, LyricsQuery query, CancellationToken ct)
            => Throw is not null ? Task.FromException<LyricifyLyricsResult?>(Throw) : Task.FromResult(Result);
    }

    private static LyricsQuery Query => new("s1", Artist: "The Weeknd", Title: "Blinding Lights",
        Album: "After Hours", DurationSeconds: 200);

    private static (QQMusicLyricsProvider Provider, FakeClient Client) NewProvider(int threshold = 55, bool enabled = true)
    {
        var log = new LoggingService();
        var registry = new LyricsParserRegistry(
            [new LyricifyWordLyricsParser(QQLyricsConstants.QrcFormat, QQLyricsConstants.ProviderId, LyricsRawTypes.Qrc, log)],
            log);
        var client = new FakeClient();
        var options = Options.Create(new QQLyricsOptions { Enabled = enabled, MatchThreshold = threshold });
        return (new QQMusicLyricsProvider(client, registry, options, log), client);
    }

    private const string QrcSample = "[0,1000]Hello(0,500)World(500,500)"; // as in Task 4

    [TestMethod]
    public async Task ProviderId_IsQq() => Assert.AreEqual("qq", NewProvider().Provider.Id);

    [TestMethod]
    public async Task Disabled_ReturnsDefinitiveEmpty()
    {
        var (provider, _) = NewProvider(enabled: false);
        var r = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
    }

    [TestMethod]
    public async Task NoCandidate_ReturnsDefinitiveEmpty()
    {
        var (provider, client) = NewProvider();
        client.Result = null;
        var r = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
    }

    [TestMethod]
    public async Task BelowThreshold_ReturnsDefinitiveEmpty()
    {
        var (provider, client) = NewProvider(threshold: 90);
        client.Result = new LyricifyLyricsResult(QrcSample, "qrc",
            new LyricsCandidate("Totally Different Song", "Other Artist", "Other", 12));
        var r = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
    }

    [TestMethod]
    public async Task GoodMatch_ReturnsWordContent()
    {
        var (provider, client) = NewProvider(threshold: 55);
        client.Result = new LyricifyLyricsResult(QrcSample, "qrc",
            new LyricsCandidate("Blinding Lights", "The Weeknd", "After Hours", 200));
        var r = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Content, r.Kind);
        Assert.AreEqual(LyricsSyncKind.Word, r.Document!.SyncKind);
        Assert.AreEqual("qrc", r.PayloadFormat);
        Assert.IsTrue(r.Definitive);
    }

    [TestMethod]
    public async Task LibraryThrows_ReturnsTransient()
    {
        var (provider, client) = NewProvider();
        client.Throw = new HttpRequestException("boom");
        var r = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~CnLyricsProviderTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Create the options**

Create `Hypersonic/Models/ExternalSearchLyricsOptions.cs`:

```csharp
namespace Hypersonic.Models;

/// <summary>
/// Shared options for a search-based external lyrics provider. <see cref="Enabled"/> mirrors
/// <c>AmllLyricsOptions</c>; <see cref="MatchThreshold"/> (0–100) is the confidence floor below which
/// a fuzzy match is rejected as <c>DefinitiveEmpty</c>.
/// </summary>
public abstract class ExternalSearchLyricsOptions
{
    public bool Enabled { get; set; } = true;
    public int MatchThreshold { get; set; } = 55;
}

public sealed class QQLyricsOptions : ExternalSearchLyricsOptions;
public sealed class NeteaseLyricsOptions : ExternalSearchLyricsOptions;
public sealed class KugouLyricsOptions : ExternalSearchLyricsOptions;
```

- [ ] **Step 4: Create the provider base**

Create `Hypersonic/Services/Lyrics/CnLyricsProviderBase.cs`:

```csharp
using System.Text;

using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Shared body for the three search-based CN providers. Honors <c>Enabled</c>, guards a blank title,
/// calls the facade, gates the candidate on <see cref="LyricsMatchScorer"/>, and parses the raw payload
/// through the shared registry. Subclasses supply only the platform, id, and options.
/// </summary>
internal abstract class CnLyricsProviderBase : ILyricsProvider
{
    private readonly ILyricifyLyricsClient _client;
    private readonly ILyricsParserRegistry _parserRegistry;
    private readonly ILogger _log;

    protected CnLyricsProviderBase(ILyricifyLyricsClient client, ILyricsParserRegistry parserRegistry, ILoggingService logService)
    {
        _client = client;
        _parserRegistry = parserRegistry;
        _log = logService.GetLogger(GetType().Name);
    }

    public abstract string Id { get; }
    protected abstract LyricifyPlatform Platform { get; }
    protected abstract bool Enabled { get; }
    protected abstract int MatchThreshold { get; }

    public async Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Enabled)
        {
            return LyricsProviderResult.DefinitiveEmpty; // inert but non-blocking to the negative cache
        }

        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return LyricsProviderResult.Transient; // nothing queryable; do not negative-cache
        }

        LyricifyLyricsResult? result;
        try
        {
            result = await _client.FetchAsync(Platform, query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — never masked
        }
        catch (Exception ex)
        {
            _log.Warning("Lyricify fetch failed (transient)", new { Id, query.SongId }, ex);
            return LyricsProviderResult.Transient;
        }

        if (result is null)
        {
            return LyricsProviderResult.DefinitiveEmpty; // no candidate from this source
        }

        var score = LyricsMatchScorer.Score(query, result.Candidate);
        if (score < MatchThreshold)
        {
            return LyricsProviderResult.DefinitiveEmpty; // not confident enough — don't hand a wrong-song match to the policy
        }

        var bytes = Encoding.UTF8.GetBytes(result.RawText);
        var document = _parserRegistry.Parse(result.FormatTag, bytes);
        if (document is null)
        {
            return LyricsProviderResult.Transient; // present-but-unparseable → never assert "no lyrics"
        }

        return LyricsProviderResult.Content(document, bytes, result.FormatTag, definitive: true);
    }
}
```

- [ ] **Step 5: Create the three subclasses**

Create `Hypersonic/Services/Lyrics/QQMusicLyricsProvider.cs`:

```csharp
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services.Lyrics;

/// <summary>The QQ Music lyrics source (QRC, word-tier).</summary>
internal sealed class QQMusicLyricsProvider : CnLyricsProviderBase
{
    private readonly QQLyricsOptions _options;

    public QQMusicLyricsProvider(ILyricifyLyricsClient client, ILyricsParserRegistry parserRegistry,
        IOptions<QQLyricsOptions> options, ILoggingService logService)
        : base(client, parserRegistry, logService)
        => _options = options.Value;

    public override string Id => QQLyricsConstants.ProviderId;
    protected override LyricifyPlatform Platform => LyricifyPlatform.QQMusic;
    protected override bool Enabled => _options.Enabled;
    protected override int MatchThreshold => _options.MatchThreshold;
}
```

Create `Hypersonic/Services/Lyrics/NeteaseLyricsProvider.cs` (identical shape, `NeteaseLyricsConstants.ProviderId`, `LyricifyPlatform.Netease`, `NeteaseLyricsOptions`):

```csharp
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services.Lyrics;

/// <summary>The Netease Cloud Music lyrics source (YRC word when present, else LRC line).</summary>
internal sealed class NeteaseLyricsProvider : CnLyricsProviderBase
{
    private readonly NeteaseLyricsOptions _options;

    public NeteaseLyricsProvider(ILyricifyLyricsClient client, ILyricsParserRegistry parserRegistry,
        IOptions<NeteaseLyricsOptions> options, ILoggingService logService)
        : base(client, parserRegistry, logService)
        => _options = options.Value;

    public override string Id => NeteaseLyricsConstants.ProviderId;
    protected override LyricifyPlatform Platform => LyricifyPlatform.Netease;
    protected override bool Enabled => _options.Enabled;
    protected override int MatchThreshold => _options.MatchThreshold;
}
```

Create `Hypersonic/Services/Lyrics/KugouLyricsProvider.cs` (identical shape, `KugouLyricsConstants.ProviderId`, `LyricifyPlatform.Kugou`, `KugouLyricsOptions`):

```csharp
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services.Lyrics;

/// <summary>The Kugou lyrics source (KRC, word-tier; the library decrypts the payload).</summary>
internal sealed class KugouLyricsProvider : CnLyricsProviderBase
{
    private readonly KugouLyricsOptions _options;

    public KugouLyricsProvider(ILyricifyLyricsClient client, ILyricsParserRegistry parserRegistry,
        IOptions<KugouLyricsOptions> options, ILoggingService logService)
        : base(client, parserRegistry, logService)
        => _options = options.Value;

    public override string Id => KugouLyricsConstants.ProviderId;
    protected override LyricifyPlatform Platform => LyricifyPlatform.Kugou;
    protected override bool Enabled => _options.Enabled;
    protected override int MatchThreshold => _options.MatchThreshold;
}
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~CnLyricsProviderTests"`
Expected: PASS (all 6). (If `GoodMatch_ReturnsWordContent` fails on the `QrcSample` not parsing, use the same corrected sample from Task 4.)

- [ ] **Step 7: Commit**

```bash
git add Hypersonic/Models/ExternalSearchLyricsOptions.cs Hypersonic/Services/Lyrics/CnLyricsProviderBase.cs Hypersonic/Services/Lyrics/QQMusicLyricsProvider.cs Hypersonic/Services/Lyrics/NeteaseLyricsProvider.cs Hypersonic/Services/Lyrics/KugouLyricsProvider.cs Hypersonic.Tests/Services/Lyrics/CnLyricsProviderTests.cs
git commit -m "feat(lyrics): add QQMusic/Netease/Kugou providers with confidence gate"
```

---

### Task 8: Cache format-extension integration

**Files:**
- Modify: `Hypersonic/Services/LyricsService.cs:475-483` (`ExtensionForFormat`)
- Modify: `Hypersonic/Services/SqliteLyricsCacheStore.cs:23-24` (`CachedLyricsExtensions`)
- Test: `Hypersonic.Tests/Services/Lyrics/LyricsCacheFormatTests.cs`

**Interfaces:**
- Consumes: Task 3 constants; `LyricsService.ExtensionForFormat` (`internal static`); `SqliteLyricsCacheStore.IsKnownLyricsFileName` (`internal static`).
- Produces: `.qrc` / `.krc` / `.yrc` / `.lrc` extensions recognized for the new format tags, so payloads are written with a correct extension and the reconcile sweep does not delete them as orphans.

- [ ] **Step 1: Write the failing tests**

Create `Hypersonic.Tests/Services/Lyrics/LyricsCacheFormatTests.cs`:

```csharp
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Services;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsCacheFormatTests
{
    [TestMethod]
    public void ExtensionForFormat_MapsNewTags()
    {
        Assert.AreEqual(".qrc", LyricsService.ExtensionForFormat(QQLyricsConstants.QrcFormat));
        Assert.AreEqual(".krc", LyricsService.ExtensionForFormat(KugouLyricsConstants.KrcFormat));
        Assert.AreEqual(".yrc", LyricsService.ExtensionForFormat(NeteaseLyricsConstants.YrcFormat));
        Assert.AreEqual(".lrc", LyricsService.ExtensionForFormat(NeteaseLyricsConstants.LrcFormat));
    }

    [TestMethod]
    public void IsKnownLyricsFileName_AcceptsQrcAndKrc()
    {
        var hash = new string('a', 64);
        Assert.IsTrue(SqliteLyricsCacheStore.IsKnownLyricsFileName(hash + ".qrc"));
        Assert.IsTrue(SqliteLyricsCacheStore.IsKnownLyricsFileName(hash + ".krc"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsCacheFormatTests"`
Expected: FAIL — `ExtensionForFormat` returns `.txt` for the new tags; `.qrc`/`.krc` are not known filenames.

- [ ] **Step 3: Extend `ExtensionForFormat`**

In `Hypersonic/Services/LyricsService.cs`, update the `ExtensionForFormat` switch (around line 475) to add the four new cases before the default:

```csharp
    internal static string ExtensionForFormat(string formatTag) => formatTag switch
    {
        SubsonicLyricsConstants.StructuredFormat => ".json",
        SubsonicLyricsConstants.LegacyFormat => ".txt",
        LrcLibLyricsConstants.SyncedFormat => ".lrc",
        LrcLibLyricsConstants.PlainFormat => ".txt",
        TtmlLyricsConstants.Format => ".ttml",
        QQLyricsConstants.QrcFormat => ".qrc",
        KugouLyricsConstants.KrcFormat => ".krc",
        NeteaseLyricsConstants.YrcFormat => ".yrc",
        NeteaseLyricsConstants.LrcFormat => ".lrc",
        _ => ".txt",
    };
```

- [ ] **Step 4: Extend the store's known-extension set**

In `Hypersonic/Services/SqliteLyricsCacheStore.cs`, update `CachedLyricsExtensions` (line 23-24) to add `.qrc` and `.krc` (`.yrc` and `.lrc` are already present):

```csharp
    private static readonly string[] CachedLyricsExtensions =
        [".json", ".txt", ".lrc", ".ttml", ".yrc", ".qrc", ".krc"];
```

Also update the doc comment above `IsKnownLyricsFileName` (line 64-65) to list the new extensions:

```csharp
    /// <summary>
    /// True for a 64-hex basename plus a known lyrics extension (<c>.json</c>/<c>.txt</c>/<c>.lrc</c>/
    /// <c>.ttml</c>/<c>.yrc</c>/<c>.qrc</c>/<c>.krc</c>). Excludes <c>cache.db</c> and unrelated files.
    /// </summary>
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsCacheFormatTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Hypersonic/Services/LyricsService.cs Hypersonic/Services/SqliteLyricsCacheStore.cs Hypersonic.Tests/Services/Lyrics/LyricsCacheFormatTests.cs
git commit -m "feat(lyrics): register qrc/krc/yrc cache extensions"
```

---

### Task 9: DI registration

**Files:**
- Modify: `Hypersonic/App.xaml.cs:120-134` (the lyrics DI block)
- Test: `Hypersonic.Tests/Services/Lyrics/LyricsProviderRegistrationTests.cs`

**Interfaces:**
- Consumes: all types from Tasks 2–7.
- Produces: the parsers, facade, providers, and options registered so the running app resolves them. Registration order sets the policy's equal-tier tiebreak.

- [ ] **Step 1: Write the failing test**

Create `Hypersonic.Tests/Services/Lyrics/LyricsProviderRegistrationTests.cs`. This test builds the same registrations the app uses and asserts the providers and parsers resolve in the expected order.

```csharp
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services.Lyrics;

using Microsoft.Extensions.DependencyInjection;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsProviderRegistrationTests
{
    private static ServiceProvider BuildLyricsSubset()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<ILyricsParserRegistry, LyricsParserRegistry>();

        // The CN word parsers + Netease LRC parser (the subset added by this feature).
        services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
            QQLyricsConstants.QrcFormat, QQLyricsConstants.ProviderId, Lyricify.Lyrics.Models.LyricsRawTypes.Qrc,
            sp.GetRequiredService<ILoggingService>()));
        services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
            KugouLyricsConstants.KrcFormat, KugouLyricsConstants.ProviderId, Lyricify.Lyrics.Models.LyricsRawTypes.Krc,
            sp.GetRequiredService<ILoggingService>()));
        services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
            NeteaseLyricsConstants.YrcFormat, NeteaseLyricsConstants.ProviderId, Lyricify.Lyrics.Models.LyricsRawTypes.Yrc,
            sp.GetRequiredService<ILoggingService>()));
        services.AddSingleton<ILyricsPayloadParser, NeteaseLrcLyricsParser>();

        services.AddSingleton<ILyricifyLyricsClient, LyricifyLyricsClient>();
        services.Configure<QQLyricsOptions>(_ => { });
        services.Configure<NeteaseLyricsOptions>(_ => { });
        services.Configure<KugouLyricsOptions>(_ => { });
        services.AddSingleton<ILyricsProvider, QQMusicLyricsProvider>();
        services.AddSingleton<ILyricsProvider, NeteaseLyricsProvider>();
        services.AddSingleton<ILyricsProvider, KugouLyricsProvider>();
        return services.BuildServiceProvider();
    }

    // NOTE: this exercises only the CN subset this feature adds, so it verifies the CN trio wires up
    // and resolves in qq → netease → kugou order. It does NOT prove the cross-provider position
    // (Subsonic → lrclib → AMLL → CN) — that lives in App.xaml.cs and is verified by inspection in Step 4.
    [TestMethod]
    public void CnProviders_ResolveInQqNeteaseKugouOrder()
    {
        using var sp = BuildLyricsSubset();
        var ids = sp.GetServices<ILyricsProvider>().Select(p => p.Id).ToArray();
        CollectionAssert.AreEqual(new[] { "qq", "netease", "kugou" }, ids);
    }

    [TestMethod]
    public void Registry_ResolvesNewFormatTags()
    {
        using var sp = BuildLyricsSubset();
        var registry = sp.GetRequiredService<ILyricsParserRegistry>();
        // A tag with no parser logs + returns null; a registered tag must at least not be "unregistered".
        // Feed empty payload: registered parsers return null (empty), unregistered also null — so assert via
        // a valid netease-lrc payload which only resolves if the parser is registered.
        var doc = registry.Parse(NeteaseLyricsConstants.LrcFormat,
            System.Text.Encoding.UTF8.GetBytes("[00:01.00]hi"));
        Assert.IsNotNull(doc);
        Assert.AreEqual("netease", doc!.SourceProvider);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsProviderRegistrationTests"`
Expected: FAIL to compile / resolve (until the types are all present — they are, from Tasks 2–7 — so this should compile; it verifies the registration wiring is correct).

- [ ] **Step 3: Wire the real registrations in `App.xaml.cs`**

In `Hypersonic/App.xaml.cs`, extend the lyrics DI block. After the `TtmlLyricsParser` registration (line 126) add the new parsers; after the `AmllTtmlLyricsProvider` registration (line 129) add the new providers; and add the facade + options. The final block reads:

```csharp
            services.AddSingleton<ILyricsPayloadParser, TtmlLyricsParser>();
            services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
                QQLyricsConstants.QrcFormat, QQLyricsConstants.ProviderId, Lyricify.Lyrics.Models.LyricsRawTypes.Qrc,
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
                KugouLyricsConstants.KrcFormat, KugouLyricsConstants.ProviderId, Lyricify.Lyrics.Models.LyricsRawTypes.Krc,
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
                NeteaseLyricsConstants.YrcFormat, NeteaseLyricsConstants.ProviderId, Lyricify.Lyrics.Models.LyricsRawTypes.Yrc,
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<ILyricsPayloadParser, NeteaseLrcLyricsParser>();

            services.AddSingleton<ILyricProvider_placeholder>(); // <-- remove this line; shown only to mark the spot
            services.AddSingleton<ILyricsProvider, SubsonicLyricsProvider>();
            services.AddSingleton<ILyricsProvider, LrcLibLyricsProvider>(); // AFTER Subsonic → Subsonic wins an equal-tier tie
            services.AddSingleton<ILyricsProvider, AmllTtmlLyricsProvider>(); // word-tier, wins only on strictly-higher tier
            services.AddSingleton<ILyricsProvider, QQMusicLyricsProvider>();  // CN word-tier, gated by confidence
            services.AddSingleton<ILyricsProvider, NeteaseLyricsProvider>();
            services.AddSingleton<ILyricsProvider, KugouLyricsProvider>();
            services.AddSingleton<IAmllLyricsIndex, AmllLyricsIndex>();
            services.AddSingleton<ILyricifyLyricsClient, LyricifyLyricsClient>();
            services.Configure<AmllLyricsOptions>(_ => { });
            services.Configure<QQLyricsOptions>(_ => { });     // defaults: Enabled=true, MatchThreshold=55
            services.Configure<NeteaseLyricsOptions>(_ => { });
            services.Configure<KugouLyricsOptions>(_ => { });
```

> Note: the existing `SubsonicLyricsProvider` / `LrcLibLyricsProvider` / `AmllTtmlLyricsProvider` lines are already present (lines 127-129) — move the three CN provider registrations to sit immediately after `AmllTtmlLyricsProvider`, keeping Subsonic → lrclib → AMLL → QQ → Netease → Kugou order. Do NOT add the `ILyricProvider_placeholder` line (it is only a marker in this plan). Add `using Hypersonic.Core.Services.Lyrics;` if not already imported for the constants.

- [ ] **Step 4: Build the app, run the registration test, and verify order by inspection**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: PASS.
Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsProviderRegistrationTests"`
Expected: PASS (both).

Then read the lyrics block in `App.xaml.cs` and confirm the six `AddSingleton<ILyricsProvider, ...>` lines are in this exact order (the unit test only covers the CN subset, so this cross-provider order must be eyeballed): `SubsonicLyricsProvider` → `LrcLibLyricsProvider` → `AmllTtmlLyricsProvider` → `QQMusicLyricsProvider` → `NeteaseLyricsProvider` → `KugouLyricsProvider`.

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/App.xaml.cs Hypersonic.Tests/Services/Lyrics/LyricsProviderRegistrationTests.cs
git commit -m "feat(lyrics): register QQ/Netease/Kugou providers, parsers, and facade in DI"
```

---

### Task 10: Unify lrclib's search selection onto the scorer + gate

**Files:**
- Modify: `Hypersonic/Services/Lyrics/LrcLibLyricsProvider.cs` (`SelectBest`, `SearchAsync`; remove the private `NameDistance`/`DurationDelta`)
- Test: `Hypersonic.Tests/Services/Lyrics/LrcLibLyricsProviderTests.cs` (re-baseline + add a below-threshold test)

**Interfaces:**
- Consumes: `LyricsMatchScorer.Score`, `LyricsCandidate` (Task 2).
- Produces: lrclib `/api/search` now picks the max-scoring candidate and returns `DefinitiveEmpty` when the best score is below a conservative threshold. `/api/get` is unchanged (exact server match, ungated).

- [ ] **Step 1: Add the below-threshold test and adjust expectations**

In `Hypersonic.Tests/Services/Lyrics/LrcLibLyricsProviderTests.cs`, add a test that a search returning only a clearly-wrong candidate is rejected:

```csharp
    [TestMethod]
    public async Task Search_AllCandidatesWrong_DefinitiveEmpty()
    {
        // Search fallback (no /api/get artist) returns a single unrelated song → below threshold → empty.
        var wrong = """[{"trackName":"Unrelated","artistName":"Nobody","albumName":"X","duration":10,"instrumental":false,"plainLyrics":"x","syncedLyrics":"[00:01.00] x"}]""";
        var p = Provider(new StubHandler { SearchResponse = Ok(wrong) });
        var r = await p.FetchAsync(new LyricsQuery("s", Artist: "The Weeknd", Title: "Blinding Lights", Album: "After Hours", DurationSeconds: 200), CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
    }
```

The existing `Get_404_FallsThroughToSearch_RanksByDurationProximity` test still holds (the closer-duration, exact title/artist candidate scores highest and clears the loose threshold). Confirm its `StubHandler` uses candidates whose title/artist match the query so the gate passes.

- [ ] **Step 2: Run to verify the new test fails**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LrcLibLyricsProviderTests"`
Expected: FAIL — `Search_AllCandidatesWrong_DefinitiveEmpty` currently returns `Content` (lrclib returns its best pick ungated).

- [ ] **Step 3: Refactor `SelectBest` + `SearchAsync` to score + gate**

In `Hypersonic/Services/Lyrics/LrcLibLyricsProvider.cs`:

1. Add a conservative threshold constant near the top of the class:

```csharp
    // Conservative (loose) gate for lrclib's fuzzy search path — bounds regression while suppressing
    // clearly-wrong search matches. /api/get (exact server match) stays ungated. Tunable.
    private const int SearchMatchThreshold = 30;
```

2. Replace `SelectBest` and its `NameDistance`/`DurationDelta` helpers with a scorer-based selection, and change `SearchAsync` to gate. Update the tail of `SearchAsync` (the `return MapRecord(SelectBest(records, query));` line) to:

```csharp
            var best = records
                .Select(r => (Record: r, Score: LyricsMatchScorer.Score(query, ToCandidate(r))))
                .OrderByDescending(x => x.Score)
                .First();

            if (best.Score < SearchMatchThreshold)
            {
                return LyricsProviderResult.DefinitiveEmpty; // no confident search match
            }

            return MapRecord(best.Record);
```

3. Add the candidate mapper and delete the now-unused `SelectBest`, `NameDistance`, and `DurationDelta` methods:

```csharp
    private static LyricsCandidate ToCandidate(LrcLibRecord r)
        => new(r.TrackName, r.ArtistName, r.AlbumName, r.Duration is { } d ? (int)Math.Round(d) : null);
```

4. Add `using Hypersonic.Core.Services.Lyrics;` (for `LyricsMatchScorer`) and ensure `LyricsCandidate` is in scope via `Hypersonic.Core.Models.Lyrics` (already imported).

> The `/api/get` path (`TryGetAsync` → `MapRecord`) is untouched — it is an exact server match and must not be gated.

- [ ] **Step 4: Run to verify all lrclib tests pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LrcLibLyricsProviderTests"`
Expected: PASS (existing tests + the new below-threshold test). If `Get_404_FallsThroughToSearch_RanksByDurationProximity` now fails on the gate, verify its candidates share the query's title/artist so the winning score clears 30; adjust the fixture's `trackName`/`artistName` to match the query if needed (the ranking assertion — which candidate wins — is what that test guards).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/Services/Lyrics/LrcLibLyricsProvider.cs Hypersonic.Tests/Services/Lyrics/LrcLibLyricsProviderTests.cs
git commit -m "refactor(lyrics): unify lrclib search selection onto LyricsMatchScorer + gate"
```

---

### Final verification

- [ ] **Full build + test sweep**

Run: `dotnet build Hypersonic.sln -p:Platform=x64`
Expected: PASS.
Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj` and `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS.

- [ ] **Manual smoke (from Task 6, Step 4)**

Launch the app, play a track present on QQ/Netease, click the lyrics **Refresh** button (5 s budget), and confirm word/line lyrics load and cache (instant on the next play). Confirm a track with no match degrades to "no lyrics" without errors.

---

## Self-Review

**Spec coverage:**
- Reuse `Lyricify.Lyrics.Helper` → Task 1 (dependency), Task 6 (facade). ✅
- QQ/Netease/Kugou providers → Task 7. ✅
- Word tier (QRC/KRC/YRC) + syllable mapper → Task 4; Netease LRC fallback → Task 5. ✅
- Main-lane only → mapper/parsers emit single-lane docs; translation/roma dropped (not read). ✅
- On by default, per-provider toggle → Task 7 options (`Enabled = true`). ✅
- Manual-refresh latency posture → no service/UI change; verified via existing `RefreshLyricsCommand` in Final smoke. ✅
- Match gate (search-based), transparent to policy → Task 2 scorer + Task 7 provider gate (verdict baked into outcome kind). ✅
- lrclib unified onto scorer + gate, `/api/get` untouched → Task 10. ✅
- Distinct SourceProvider ids + format tags; raw payload cached → Task 3 constants, parser `SourceProvider` stamps, Task 8 extensions. ✅
- Registration order Subsonic → lrclib → AMLL → QQ → Netease → Kugou → Task 9. ✅
- Below-threshold = `DefinitiveEmpty` (epoch invalidation deferred) → Task 7 provider; not built. ✅
- `QQ` double-capital naming; lowercase ids → enforced in Tasks 3/7. ✅

**Placeholder scan:** No "TBD"/"implement later". The one marker line (`ILyricProvider_placeholder`) in Task 9 Step 3 is explicitly called out to be removed and is not real code. Library-member and syllable-type caveats are concrete "verify against IntelliSense" instructions, not blanks. Test fixtures for QRC carry an explicit "replace with a captured demo sample if it doesn't parse" fallback (the TDD loop is the oracle).

**Type consistency:** `LyricsCandidate(Title, Artist, Album, DurationSeconds)` used identically in Task 2 tests, the facade (Task 6), and `ToCandidate` (Task 10). `LyricsMatchScorer.Score(LyricsQuery, LyricsCandidate)` signature consistent across Tasks 2/7/10. Format tags (`"qrc"`/`"krc"`/`"yrc"`/`"netease-lrc"`) consistent across Tasks 3/4/5/6/8/9. `LyricifyLyricsResult(RawText, FormatTag, Candidate)` consistent between Tasks 6 and 7. Provider ids `"qq"`/`"netease"`/`"kugou"` consistent across Tasks 3/7/9.
