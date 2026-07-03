# AMLL TTML DB Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add [amll-ttml-db](https://github.com/amll-dev/amll-ttml-db) as a third, Word-tier `ILyricsProvider`, backed by a locally-cached, background-refreshed index with strict title/artist matching.

**Architecture:** A pure Core matcher (`AmllLyricsIndexData`) owns index parsing + strict lookup; a WinUI singleton (`AmllLyricsIndex`) wraps it with disk cache + ETag-conditional CDN refresh; a thin provider (`AmllTtmlLyricsProvider`) resolves → GETs the raw TTML → parses via the existing shared `TtmlLyricsParser` → maps to a `LyricsProviderResult`. The shipped fan-out loses its Word early-exit so cross-provider priority (tier, then registration order) decides every tie.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.2.0), `System.Text.Json`, `System.Text.RegularExpressions`, `IHttpClientFactory`, MSTest.

## Global Constraints

- **Code style:** 4-space indent, Allman braces, file-scoped namespaces, `var`, CRLF, interfaces `I`-prefixed. (`.editorconfig`)
- **Provider id:** `"amll-ttml-db"`. **Format tag:** reuse existing `TtmlLyricsConstants.Format` (`"ttml"`) — do not invent a new tag or parser.
- **CDN base:** `https://cdn.jsdelivr.net/gh/amll-dev/amll-ttml-db@main/` (trailing slash). Index path `metadata/raw-lyrics-index.jsonl`; payload path `raw-lyrics/<rawLyricFile>`.
- **Refresh interval default:** 7 days. **`Enabled` default:** `true` (the seam for the deferred settings toggle).
- **Normalization (identical for index and query):** Unicode NFKC → `ToLowerInvariant()` → `Trim()` → collapse internal whitespace to single space.
- **Match rule (strict):** exact normalized-title bucket hit **AND** non-empty artist-set intersection. Query artist string is *split* on `, / & ; 、 × feat. ft.`; index `artists[]` elements are normalized whole (not split). A null/blank query artist → no match.
- **Registration order:** register `AmllTtmlLyricsProvider` as `ILyricsProvider` **after** `SubsonicLyricsProvider` and `LrcLibLyricsProvider`.
- **Commits:** Conventional Commits with scope, e.g. `feat(lyrics): …`.
- **Test commands:** Core → `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`. WinUI → `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`.
- **Design source:** [`docs/design/service/2026-07-03-amll-ttml-db-provider.md`](../../design/service/2026-07-03-amll-ttml-db-provider.md).

---

### Task 1: `AmllLyricsIndexData` — pure parse + strict matcher (Core)

The correctness crux. Pure, no HTTP/disk — the bulk of the matching tests live here.

**Files:**
- Create: `Hypersonic.Core/Services/Lyrics/AmllLyricsIndexData.cs`
- Test: `Hypersonic.Core.Tests/Services/Lyrics/AmllLyricsIndexDataTests.cs`

**Interfaces:**
- Consumes: `Hypersonic.Core.Models.Lyrics.LyricsQuery` (`SongId`, `Artist?`, `Title?`, `Album?`, `DurationSeconds?`).
- Produces:
  - `static AmllLyricsIndexData Parse(ReadOnlyMemory<byte> jsonl)`
  - `string? TryResolve(LyricsQuery query)` — returns the matched `rawLyricFile`, or `null`
  - `int Count { get; }`
  - `internal static string Normalize(string s)`
  - `internal static HashSet<string> SplitQueryArtists(string? artist)`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class AmllLyricsIndexDataTests
{
    // Real index-line shape: metadata is an array of [key, [values]] pairs.
    private const string Jsonl = """
        {"metadata":[["album",["Idol"]],["artists",["YOASOBI"]],["musicName",["Idol"]],["ncmMusicId",["1"]]],"rawLyricFile":"idol-a.ttml"}
        {"metadata":[["album",["Idol"]],["artists",["YOASOBI"]],["musicName",["Idol"]],["ncmMusicId",["2"]]],"rawLyricFile":"idol-b.ttml"}
        {"metadata":[["album",["アイドル"]],["artists",["YOASOBI"]],["musicName",["アイドル"]]],"rawLyricFile":"idol-jp.ttml"}
        {"metadata":[["album",["Wishes"]],["artists",["Famba","Eric Lumiere","Raven & Kreyn"]],["musicName",["In My Head"]]],"rawLyricFile":"inmyhead.ttml"}
        """;

    private static AmllLyricsIndexData Data() => AmllLyricsIndexData.Parse(Encoding.UTF8.GetBytes(Jsonl));

    [TestMethod]
    public void Parse_CountsEntries()
        => Assert.AreEqual(4, Data().Count);

    [TestMethod]
    public void Resolve_ExactTitleAndArtist_Hits()
        => Assert.IsNotNull(Data().TryResolve(new LyricsQuery("s", Artist: "YOASOBI", Title: "Idol")));

    [TestMethod]
    public void Resolve_TitleCaseAndWidthInsensitive()
        => Assert.IsNotNull(Data().TryResolve(new LyricsQuery("s", Artist: "yoasobi", Title: "ＩＤＯＬ")));

    [TestMethod]
    public void Resolve_WrongArtist_NoMatch()
        => Assert.IsNull(Data().TryResolve(new LyricsQuery("s", Artist: "Someone Else", Title: "Idol")));

    [TestMethod]
    public void Resolve_MissingArtist_NoMatch()
        => Assert.IsNull(Data().TryResolve(new LyricsQuery("s", Artist: null, Title: "Idol")));

    [TestMethod]
    public void Resolve_DistinctTitleBucket_DoesNotCrossMatch()
    {
        // Japanese title lives in its own bucket; querying it never returns the English file.
        var file = Data().TryResolve(new LyricsQuery("s", Artist: "YOASOBI", Title: "アイドル"));
        Assert.AreEqual("idol-jp.ttml", file);
    }

    [TestMethod]
    public void Resolve_AlbumTiebreak_PrefersMatchingAlbum()
    {
        // Both idol-a/idol-b share title+artist+album "Idol"; with the album present the first is fine,
        // but a query for a non-existent album still falls back to the first candidate (not null).
        var file = Data().TryResolve(new LyricsQuery("s", Artist: "YOASOBI", Title: "Idol", Album: "Idol"));
        Assert.IsTrue(file is "idol-a.ttml" or "idol-b.ttml");
    }

    [TestMethod]
    public void Resolve_QueryArtistFeatSplit_IntersectsIndexArray()
    {
        // Subsonic crams a collab into one string; the index has it as separate array elements.
        var file = Data().TryResolve(new LyricsQuery("s", Artist: "Famba feat. Eric Lumiere", Title: "In My Head"));
        Assert.AreEqual("inmyhead.ttml", file);
    }

    [TestMethod]
    public void Parse_SkipsMalformedLines()
    {
        var data = AmllLyricsIndexData.Parse(Encoding.UTF8.GetBytes("not json\n" + Jsonl.Split('\n')[0]));
        Assert.AreEqual(1, data.Count);
    }

    [TestMethod]
    public void Parse_SkipsWrongShapeMetadata_DoesNotThrow()
    {
        // Structurally valid JSON but musicName is a scalar (not an array): must be skipped, never throw
        // (guards against JsonElement accessors raising InvalidOperationException and losing the whole index).
        var bad = """{"metadata":[["musicName","Idol"],["artists",["A"]]],"rawLyricFile":"x.ttml"}""";
        var data = AmllLyricsIndexData.Parse(Encoding.UTF8.GetBytes(bad + "\n" + Jsonl.Split('\n')[0]));
        Assert.AreEqual(1, data.Count); // wrong-shape line dropped; the good line kept
    }

    [TestMethod]
    public void SplitQueryArtists_SplitsSeparators()
    {
        var set = AmllLyricsIndexData.SplitQueryArtists("A, B & C feat. D");
        CollectionAssert.AreEquivalent(new[] { "a", "b", "c", "d" }, set.ToArray());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~AmllLyricsIndexDataTests"`
Expected: FAIL — `AmllLyricsIndexData` does not exist (compile error).

- [ ] **Step 3: Implement `AmllLyricsIndexData`**

```csharp
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Services.Lyrics;

/// <summary>
/// Pure, immutable parsed view of amll-ttml-db's <c>raw-lyrics-index.jsonl</c> plus the strict
/// title/artist matcher. No HTTP, no disk — the WinUI <c>AmllLyricsIndex</c> supplies the bytes.
/// Keyed by normalized title; each bucket holds every submission (duplicates and title collisions),
/// disambiguated by artist-set intersection at lookup.
/// </summary>
public sealed class AmllLyricsIndexData
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ArtistSeparators =
        new(@"\s*(?:[,/&;、×]|\bfeat\.?|\bft\.?)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<string, List<Entry>> _byTitle;

    private AmllLyricsIndexData(Dictionary<string, List<Entry>> byTitle, int count)
    {
        _byTitle = byTitle;
        Count = count;
    }

    /// <summary>Total number of index entries parsed (across all title buckets).</summary>
    public int Count { get; }

    public static AmllLyricsIndexData Parse(ReadOnlyMemory<byte> jsonl)
    {
        var byTitle = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        var count = 0;

        foreach (var line in Encoding.UTF8.GetString(jsonl.Span).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (TryParseLine(trimmed, out var title, out var entry))
            {
                if (!byTitle.TryGetValue(title, out var bucket))
                {
                    bucket = [];
                    byTitle[title] = bucket;
                }

                bucket.Add(entry);
                count++;
            }
        }

        return new AmllLyricsIndexData(byTitle, count);
    }

    /// <summary>Resolves the best <c>rawLyricFile</c> for a query, or <c>null</c> when strict matching fails.</summary>
    public string? TryResolve(LyricsQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return null;
        }

        if (!_byTitle.TryGetValue(Normalize(query.Title), out var candidates))
        {
            return null;
        }

        var queryArtists = SplitQueryArtists(query.Artist);
        if (queryArtists.Count == 0)
        {
            return null; // strict: no artist to confirm against
        }

        List<Entry>? matches = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Artists.Overlaps(queryArtists))
            {
                (matches ??= []).Add(candidate);
            }
        }

        if (matches is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(query.Album))
        {
            var album = Normalize(query.Album);
            foreach (var match in matches)
            {
                if (match.Album == album)
                {
                    return match.RawLyricFile;
                }
            }
        }

        return matches[0].RawLyricFile;
    }

    private static bool TryParseLine(string line, out string normalizedTitle, out Entry entry)
    {
        normalizedTitle = string.Empty;
        entry = default!;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("rawLyricFile", out var fileEl) || fileEl.GetString() is not { } file)
            {
                return false;
            }

            string? title = null, album = null;
            var artists = new HashSet<string>(StringComparer.Ordinal);

            if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Array)
            {
                foreach (var pair in metadata.EnumerateArray())
                {
                    if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2)
                    {
                        continue;
                    }

                    var key = pair[0].ValueKind == JsonValueKind.String ? pair[0].GetString() : null;
                    var values = pair[1];
                    switch (key)
                    {
                        case "musicName" when values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0:
                            title = values[0].GetString();
                            break;
                        case "album" when values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0:
                            album = values[0].GetString();
                            break;
                        case "artists" when values.ValueKind == JsonValueKind.Array:
                            foreach (var a in values.EnumerateArray())
                            {
                                if (a.GetString() is { } name)
                                {
                                    var normalized = Normalize(name);
                                    if (normalized.Length > 0)
                                    {
                                        artists.Add(normalized);
                                    }
                                }
                            }
                            break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            normalizedTitle = Normalize(title);
            entry = new Entry(file, artists, string.IsNullOrWhiteSpace(album) ? null : Normalize(album));
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false; // skip malformed or wrong-shape lines (bad JSON, or unexpected JSON value kinds)
        }
    }

    internal static string Normalize(string s)
    {
        var normalized = s.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim();
        return WhitespaceRun.Replace(normalized, " ");
    }

    /// <summary>Splits a Subsonic artist string on collab separators into a normalized token set.</summary>
    internal static HashSet<string> SplitQueryArtists(string? artist)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(artist))
        {
            return set;
        }

        foreach (var part in ArtistSeparators.Split(artist))
        {
            var normalized = Normalize(part);
            if (normalized.Length > 0)
            {
                set.Add(normalized);
            }
        }

        return set;
    }

    private sealed record Entry(string RawLyricFile, HashSet<string> Artists, string? Album);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj --filter "FullyQualifiedName~AmllLyricsIndexDataTests"`
Expected: PASS (all 11 tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic.Core/Services/Lyrics/AmllLyricsIndexData.cs Hypersonic.Core.Tests/Services/Lyrics/AmllLyricsIndexDataTests.cs
git commit -m "feat(lyrics): AMLL index parser + strict title/artist matcher"
```

---

### Task 2: Constants, options, interface, and `AmllLyricsIndex` disk-load + readiness (WinUI)

Scaffolding plus the disk-load half of the index shell. No network yet.

**Files:**
- Create: `Hypersonic/Services/Lyrics/AmllLyricsConstants.cs`
- Create: `Hypersonic/Models/AmllLyricsOptions.cs`
- Create: `Hypersonic/Contracts/Services/IAmllLyricsIndex.cs`
- Create: `Hypersonic/Services/Lyrics/AmllLyricsIndex.cs`
- Test: `Hypersonic.Tests/Services/Lyrics/AmllLyricsIndexTests.cs`

**Interfaces:**
- Consumes: `IAppFolderLocator.GetCacheFolder()`, `IHttpClientFactory`, `TimeProvider`, `ILoggingService`, `IOptions<AmllLyricsOptions>`, `AmllLyricsIndexData` (Task 1).
- Produces:
  - `AmllLyricsConstants.ProviderId` (`"amll-ttml-db"`), `.CdnBase`, `.IndexPath`, `.PayloadPathPrefix`, `.CacheSubfolder`, `.HttpClientName` (`"amll"`)
  - `AmllLyricsOptions { int RefreshIntervalDays = 7; bool Enabled = true; }`
  - `interface IAmllLyricsIndex { bool IsReady { get; } string? TryResolve(LyricsQuery query); Task WarmAsync(CancellationToken cancellationToken = default); }`
  - `AmllLyricsIndex : IAmllLyricsIndex` (this task: disk-load + readiness only)

- [ ] **Step 1: Create the constants, options, and interface**

`Hypersonic/Services/Lyrics/AmllLyricsConstants.cs`:

```csharp
namespace Hypersonic.Services.Lyrics;

/// <summary>Constants for the amll-ttml-db source: its provider id, CDN mirror layout, and cache location.</summary>
internal static class AmllLyricsConstants
{
    public const string ProviderId = "amll-ttml-db";
    public const string HttpClientName = "amll";
    public const string CdnBase = "https://cdn.jsdelivr.net/gh/amll-dev/amll-ttml-db@main/";
    public const string IndexPath = "metadata/raw-lyrics-index.jsonl";
    public const string PayloadPathPrefix = "raw-lyrics/";
    public const string CacheSubfolder = "AmllIndex";
    public const string IndexFileName = "raw-lyrics-index.jsonl";
    public const string MetaFileName = "index.meta.json";
}
```

`Hypersonic/Models/AmllLyricsOptions.cs`:

```csharp
namespace Hypersonic.Models;

/// <summary>
/// Options for the amll-ttml-db provider. <see cref="Enabled"/> is the seam for the deferred
/// legal-posture settings toggle; it defaults to <c>true</c> (always-on for now).
/// </summary>
public class AmllLyricsOptions
{
    /// <summary>Disk-index staleness threshold that triggers a conditional refresh.</summary>
    public int RefreshIntervalDays { get; set; } = 7;

    /// <summary>When false, the provider is inert and the index does no network/disk work.</summary>
    public bool Enabled { get; set; } = true;
}
```

`Hypersonic/Contracts/Services/IAmllLyricsIndex.cs`:

```csharp
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Contracts.Services;

/// <summary>
/// The amll-ttml-db index: a locally-cached, background-refreshed lookup from a track to its raw
/// TTML file name. Single writer; the provider is a pure reader via <see cref="TryResolve"/>.
/// </summary>
public interface IAmllLyricsIndex
{
    /// <summary>True once an index (from disk or network) is loaded and queryable.</summary>
    bool IsReady { get; }

    /// <summary>Resolves a track to its <c>rawLyricFile</c>, or <c>null</c> if not ready or no match.</summary>
    string? TryResolve(LyricsQuery query);

    /// <summary>Loads the disk index if present, then refreshes from the CDN when absent or stale.</summary>
    Task WarmAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing disk-load tests**

`Hypersonic.Tests/Services/Lyrics/AmllLyricsIndexTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Models;
using Hypersonic.Services.Lyrics;
using Hypersonic.Tests.Fakes;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class AmllLyricsIndexTests
{
    private string _dir = null!;
    private FakeTimeProvider _time = null!;

    private const string Jsonl =
        """{"metadata":[["artists",["YOASOBI"]],["musicName",["Idol"]]],"rawLyricFile":"idol.ttml"}""";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "amll-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private AmllLyricsIndex Index(StubHandler handler, bool enabled = true)
        => new(new StubFactory(handler), new FakeFolderLocator(_dir),
            Options.Create(new AmllLyricsOptions { Enabled = enabled }), _time, new NullLoggingService());

    private static LyricsQuery Idol => new("s", Artist: "YOASOBI", Title: "Idol");

    [TestMethod]
    public void BeforeWarm_NotReady_ResolveNull()
    {
        var index = Index(new StubHandler());
        Assert.IsFalse(index.IsReady);
        Assert.IsNull(index.TryResolve(Idol));
    }

    [TestMethod]
    public async Task Warm_FreshDiskIndex_LoadsWithoutNetwork()
    {
        // Seed a fresh disk index + sidecar stamped "now".
        var sub = Path.Combine(_dir, AmllLyricsConstants.CacheSubfolder);
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, AmllLyricsConstants.IndexFileName), Jsonl);
        File.WriteAllText(Path.Combine(sub, AmllLyricsConstants.MetaFileName),
            JsonSerializer.Serialize(new { ETag = "\"e1\"", FetchedUtc = _time.GetUtcNow() }));

        var handler = new StubHandler();
        var index = Index(handler);
        await index.WarmAsync();

        Assert.IsTrue(index.IsReady);
        Assert.AreEqual("idol.ttml", index.TryResolve(Idol));
        Assert.AreEqual(0, handler.Requests); // fresh → no network
    }

    // ---- shared stub HTTP harness (reused by Task 3) ----

    internal sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage? Response { get; set; }
        public int Requests { get; private set; }
        public string? LastIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            LastIfNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault()?.Tag;
            return Task.FromResult(Response ?? new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Jsonl, Encoding.UTF8, "application/json") });
        }
    }

    internal sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri(AmllLyricsConstants.CdnBase) };
    }

    internal sealed class FakeFolderLocator : IAppFolderLocator
    {
        private readonly string _cache;
        public FakeFolderLocator(string cache) => _cache = cache;
        public string GetCacheFolder() => _cache;
        public string GetLyricsOverrideFolder() => Path.Combine(_cache, "LyricsOverride");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AmllLyricsIndexTests"`
Expected: FAIL — `AmllLyricsIndex` does not exist.

- [ ] **Step 4: Implement `AmllLyricsIndex` (disk-load + readiness; network is a stub for now)**

```csharp
using System.Text.Json;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Stateful shell around <see cref="AmllLyricsIndexData"/>: owns the disk cache, the ETag-conditional
/// CDN refresh, and readiness. Single writer, singleton. See the design doc for lifecycle.
/// </summary>
public sealed class AmllLyricsIndex : IAmllLyricsIndex
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppFolderLocator _folderLocator;
    private readonly AmllLyricsOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _log;
    private volatile AmllLyricsIndexData? _data;

    public AmllLyricsIndex(
        IHttpClientFactory httpClientFactory,
        IAppFolderLocator folderLocator,
        IOptions<AmllLyricsOptions> options,
        TimeProvider timeProvider,
        ILoggingService logService)
    {
        _httpClientFactory = httpClientFactory;
        _folderLocator = folderLocator;
        _options = options.Value;
        _timeProvider = timeProvider;
        _log = logService.GetLogger(nameof(AmllLyricsIndex));
    }

    public bool IsReady => _data is not null;

    public string? TryResolve(LyricsQuery query) => _data?.TryResolve(query);

    public async Task WarmAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var (bytes, meta) = TryReadDisk();
        if (bytes is not null)
        {
            _data = AmllLyricsIndexData.Parse(bytes);
        }

        var stale = meta is null
            || _timeProvider.GetUtcNow() - meta.FetchedUtc > TimeSpan.FromDays(_options.RefreshIntervalDays);
        if (bytes is not null && !stale)
        {
            return; // fresh disk copy — no network
        }

        await RefreshFromNetworkAsync(meta?.ETag, cancellationToken).ConfigureAwait(false);
    }

    // RefreshFromNetworkAsync is a no-op stub in this task; Task 3 implements it.
    private Task RefreshFromNetworkAsync(string? etag, CancellationToken cancellationToken) => Task.CompletedTask;

    private (byte[]? Bytes, AmllIndexMeta? Meta) TryReadDisk()
    {
        try
        {
            var sub = Path.Combine(_folderLocator.GetCacheFolder(), AmllLyricsConstants.CacheSubfolder);
            var indexPath = Path.Combine(sub, AmllLyricsConstants.IndexFileName);
            var metaPath = Path.Combine(sub, AmllLyricsConstants.MetaFileName);
            if (!File.Exists(indexPath))
            {
                return (null, null);
            }

            var bytes = File.ReadAllBytes(indexPath);
            AmllIndexMeta? meta = null;
            if (File.Exists(metaPath))
            {
                meta = JsonSerializer.Deserialize<AmllIndexMeta>(File.ReadAllBytes(metaPath));
            }

            return (bytes, meta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Warning("Reading cached AMLL index failed; treating as absent", exception: ex);
            return (null, null);
        }
    }

    internal sealed record AmllIndexMeta(string? ETag, DateTimeOffset FetchedUtc);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AmllLyricsIndexTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add Hypersonic/Services/Lyrics/AmllLyricsConstants.cs Hypersonic/Models/AmllLyricsOptions.cs Hypersonic/Contracts/Services/IAmllLyricsIndex.cs Hypersonic/Services/Lyrics/AmllLyricsIndex.cs Hypersonic.Tests/Services/Lyrics/AmllLyricsIndexTests.cs
git commit -m "feat(lyrics): AMLL index shell with disk-load + readiness"
```

---

### Task 3: `AmllLyricsIndex` — CDN download + ETag conditional refresh (WinUI)

Fill in the network half: download when absent/stale, atomic disk replace, and 304 handling.

**Files:**
- Modify: `Hypersonic/Services/Lyrics/AmllLyricsIndex.cs` (replace the `RefreshFromNetworkAsync` stub; add disk-write helpers)
- Test: `Hypersonic.Tests/Services/Lyrics/AmllLyricsIndexTests.cs` (add cases; reuse the Task 2 stub harness)

**Interfaces:**
- Consumes: the `"amll"` named `HttpClient` (base = `AmllLyricsConstants.CdnBase`), `AmllIndexMeta` (Task 2).
- Produces: no new public surface — behavior only.

- [ ] **Step 1: Write the failing refresh tests**

Add to `AmllLyricsIndexTests`:

```csharp
[TestMethod]
public async Task Warm_EmptyDir_Downloads_BecomesReady_PersistsToDisk()
{
    var handler = new StubHandler
    {
        Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Jsonl, Encoding.UTF8, "application/json"),
            Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"") },
        },
    };
    var index = Index(handler);
    await index.WarmAsync();

    Assert.IsTrue(index.IsReady);
    Assert.AreEqual("idol.ttml", index.TryResolve(Idol));
    Assert.AreEqual(1, handler.Requests);
    Assert.IsTrue(File.Exists(Path.Combine(_dir, AmllLyricsConstants.CacheSubfolder, AmllLyricsConstants.IndexFileName)));
}

[TestMethod]
public async Task Warm_StaleDisk_304_KeepsDiskData()
{
    var sub = Path.Combine(_dir, AmllLyricsConstants.CacheSubfolder);
    Directory.CreateDirectory(sub);
    File.WriteAllText(Path.Combine(sub, AmllLyricsConstants.IndexFileName), Jsonl);
    // Stamp the sidecar 30 days ago → stale → conditional GET.
    var old = _time.GetUtcNow().AddDays(-30);
    File.WriteAllText(Path.Combine(sub, AmllLyricsConstants.MetaFileName),
        JsonSerializer.Serialize(new { ETag = "\"e1\"", FetchedUtc = old }));

    var handler = new StubHandler { Response = new HttpResponseMessage(HttpStatusCode.NotModified) };
    var index = Index(handler);
    await index.WarmAsync();

    Assert.IsTrue(index.IsReady);
    Assert.AreEqual("idol.ttml", index.TryResolve(Idol));
    Assert.AreEqual(1, handler.Requests);
    Assert.AreEqual("\"e1\"", handler.LastIfNoneMatch); // sent the stored ETag
}

[TestMethod]
public async Task Warm_Disabled_DoesNothing()
{
    var handler = new StubHandler();
    var index = Index(handler, enabled: false);
    await index.WarmAsync();

    Assert.IsFalse(index.IsReady);
    Assert.AreEqual(0, handler.Requests);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AmllLyricsIndexTests"`
Expected: FAIL — `Warm_EmptyDir_*` not ready / 0 files written (the stub `RefreshFromNetworkAsync` does nothing yet).

- [ ] **Step 3: Replace the `RefreshFromNetworkAsync` stub with the real implementation**

```csharp
private async Task RefreshFromNetworkAsync(string? etag, CancellationToken cancellationToken)
{
    try
    {
        var client = _httpClientFactory.CreateClient(AmllLyricsConstants.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, AmllLyricsConstants.IndexPath);
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.IfNoneMatch.Add(System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag));
        }

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            WriteMeta(new AmllIndexMeta(etag, now)); // keep disk data, restamp freshness
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            _log.Warning("AMLL index refresh non-success; keeping existing index",
                new { Status = (int)response.StatusCode });
            return;
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var data = AmllLyricsIndexData.Parse(body);
        WriteIndexAtomic(body);
        WriteMeta(new AmllIndexMeta(response.Headers.ETag?.ToString(), now));
        _data = data; // atomic swap after a successful parse + persist
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        _log.Warning("AMLL index refresh failed; keeping existing index", exception: ex);
    }
}

private void WriteIndexAtomic(byte[] body)
{
    var sub = Path.Combine(_folderLocator.GetCacheFolder(), AmllLyricsConstants.CacheSubfolder);
    Directory.CreateDirectory(sub);
    var target = Path.Combine(sub, AmllLyricsConstants.IndexFileName);
    var temp = target + ".tmp";
    File.WriteAllBytes(temp, body);
    File.Move(temp, target, overwrite: true);
}

private void WriteMeta(AmllIndexMeta meta)
{
    // Bare JsonSerializer (not the project Json helper) is deliberate and self-consistent here: the
    // sidecar is PascalCase, round-trips write↔read cleanly, and has no enums needing a custom converter.
    var sub = Path.Combine(_folderLocator.GetCacheFolder(), AmllLyricsConstants.CacheSubfolder);
    Directory.CreateDirectory(sub);
    File.WriteAllBytes(Path.Combine(sub, AmllLyricsConstants.MetaFileName), JsonSerializer.SerializeToUtf8Bytes(meta));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AmllLyricsIndexTests"`
Expected: PASS (5 tests total).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/Services/Lyrics/AmllLyricsIndex.cs Hypersonic.Tests/Services/Lyrics/AmllLyricsIndexTests.cs
git commit -m "feat(lyrics): AMLL index CDN download + ETag conditional refresh"
```

---

### Task 4: `AmllTtmlLyricsProvider` (WinUI)

The thin `ILyricsProvider`: resolve → GET → parse → map.

**Files:**
- Create: `Hypersonic/Services/Lyrics/AmllTtmlLyricsProvider.cs`
- Test: `Hypersonic.Tests/Services/Lyrics/AmllTtmlLyricsProviderTests.cs`

**Interfaces:**
- Consumes: `IAmllLyricsIndex` (Task 2), `IHttpClientFactory`, `ILyricsParserRegistry.Parse(string, ReadOnlyMemory<byte>)`, `TtmlLyricsConstants.Format`, `IOptions<AmllLyricsOptions>`.
- Produces: `AmllTtmlLyricsProvider : ILyricsProvider` — `Id => "amll-ttml-db"`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Text;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services.Lyrics;

using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class AmllTtmlLyricsProviderTests
{
    // Minimal AMLL-flavoured word-synced TTML the real TtmlLyricsParser accepts.
    private const string Ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body><div>
        <p begin="0:00.000" end="0:01.000"><span begin="0:00.000" end="0:01.000">Hi</span></p>
        </div></body></tt>
        """;

    private static AmllTtmlLyricsProvider Provider(FakeIndex index, StubHandler handler, bool enabled = true)
    {
        var log = new LoggingService();
        var registry = new LyricsParserRegistry([new TtmlLyricsParser(log)], log);
        return new AmllTtmlLyricsProvider(new StubFactory(handler), index, registry,
            Options.Create(new AmllLyricsOptions { Enabled = enabled }), log);
    }

    private static LyricsQuery Query => new("s", Artist: "A", Title: "T");

    [TestMethod]
    public void ProviderId_IsAmll()
        => Assert.AreEqual("amll-ttml-db", Provider(new FakeIndex(), new StubHandler()).Id);

    [TestMethod]
    public async Task Match_FetchesTtml_ReturnsWordContent()
    {
        var p = Provider(new FakeIndex { Ready = true, File = "idol.ttml" },
            new StubHandler { Response = Ok(Ttml) });
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Content, r.Kind);
        Assert.AreEqual(LyricsSyncKind.Word, r.Document!.SyncKind);
        Assert.AreEqual("ttml", r.PayloadFormat);
        Assert.IsTrue(r.Definitive);
    }

    [TestMethod]
    public async Task IndexNotReady_Transient_NoHttp()
    {
        var handler = new StubHandler();
        var r = await Provider(new FakeIndex { Ready = false }, handler).FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
        Assert.AreEqual(0, handler.Requests);
    }

    [TestMethod]
    public async Task NoMatch_DefinitiveEmpty_NoHttp()
    {
        var handler = new StubHandler();
        var r = await Provider(new FakeIndex { Ready = true, File = null }, handler).FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
        Assert.AreEqual(0, handler.Requests);
    }

    [TestMethod]
    public async Task BlankTitle_Transient_NoHttp()
    {
        var handler = new StubHandler();
        var r = await Provider(new FakeIndex { Ready = true, File = "x.ttml" }, handler)
            .FetchAsync(new LyricsQuery("s", Artist: "A", Title: " "), CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
        Assert.AreEqual(0, handler.Requests);
    }

    [TestMethod]
    public async Task HttpFailure_Transient()
    {
        var p = Provider(new FakeIndex { Ready = true, File = "idol.ttml" },
            new StubHandler { Response = new HttpResponseMessage(HttpStatusCode.InternalServerError) });
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
    }

    [TestMethod]
    public async Task ParseFailure_Transient()
    {
        var p = Provider(new FakeIndex { Ready = true, File = "idol.ttml" },
            new StubHandler { Response = Ok("<tt><body/></tt>") }); // no <p> → parser returns null
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
    }

    [TestMethod]
    public async Task Disabled_DefinitiveEmpty_NoIndexNoHttp()
    {
        var handler = new StubHandler();
        var index = new FakeIndex { Ready = true, File = "idol.ttml" };
        var r = await Provider(index, handler, enabled: false).FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind); // inert, non-blocking to negative cache
        Assert.AreEqual(0, handler.Requests);
        Assert.AreEqual(0, index.ResolveCalls);
    }

    [TestMethod]
    public async Task AlreadyCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await Provider(new FakeIndex { Ready = true, File = "x.ttml" }, new StubHandler())
                .FetchAsync(Query, cts.Token));
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/xml") };

    private sealed class FakeIndex : IAmllLyricsIndex
    {
        public bool Ready { get; set; }
        public string? File { get; set; }
        public int ResolveCalls { get; private set; }
        public bool IsReady => Ready;
        public string? TryResolve(LyricsQuery query) { ResolveCalls++; return File; }
        public Task WarmAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage? Response { get; set; }
        public int Requests { get; private set; }
        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            LastPath = request.RequestUri!.AbsolutePath;
            return Task.FromResult(Response ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri(AmllLyricsConstants.CdnBase) };
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AmllTtmlLyricsProviderTests"`
Expected: FAIL — `AmllTtmlLyricsProvider` does not exist.

- [ ] **Step 3: Implement `AmllTtmlLyricsProvider`**

```csharp
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// The amll-ttml-db lyrics source. Thin: it delegates matching to <see cref="IAmllLyricsIndex"/>,
/// GETs the matched raw TTML from the CDN, and parses it through the shared registry (word-synced,
/// top tier). All matching/index policy lives in the index; all TTML parsing in the shared parser.
/// </summary>
internal sealed class AmllTtmlLyricsProvider : ILyricsProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAmllLyricsIndex _index;
    private readonly ILyricsParserRegistry _parserRegistry;
    private readonly AmllLyricsOptions _options;
    private readonly ILogger _log;

    public AmllTtmlLyricsProvider(
        IHttpClientFactory httpClientFactory,
        IAmllLyricsIndex index,
        ILyricsParserRegistry parserRegistry,
        IOptions<AmllLyricsOptions> options,
        ILoggingService logService)
    {
        _httpClientFactory = httpClientFactory;
        _index = index;
        _parserRegistry = parserRegistry;
        _options = options.Value;
        _log = logService.GetLogger(nameof(AmllTtmlLyricsProvider));
    }

    public string Id => AmllLyricsConstants.ProviderId;

    public async Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Disabled → inert but non-blocking to the negative cache (a permanent Transient would block it).
        if (!_options.Enabled)
        {
            return LyricsProviderResult.DefinitiveEmpty;
        }

        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return LyricsProviderResult.Transient; // nothing queryable; do not negative-cache
        }

        if (!_index.IsReady)
        {
            return LyricsProviderResult.Transient; // warming — retried next track, no negative poisoning
        }

        var file = _index.TryResolve(query);
        if (file is null)
        {
            return LyricsProviderResult.DefinitiveEmpty; // this source honestly has no entry
        }

        try
        {
            var client = _httpClientFactory.CreateClient(AmllLyricsConstants.HttpClientName);
            using var response = await client.GetAsync(AmllLyricsConstants.PayloadPathPrefix + file, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return LyricsProviderResult.Transient;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var document = _parserRegistry.Parse(TtmlLyricsConstants.Format, bytes);
            if (document is null)
            {
                // Present-but-unparseable payload → parse failure → Transient (never assert "no lyrics").
                return LyricsProviderResult.Transient;
            }

            return LyricsProviderResult.Content(document, bytes, TtmlLyricsConstants.Format, definitive: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — never masked as a provider transient
        }
        catch (Exception ex)
        {
            _log.Warning("AMLL payload fetch failed (transient)", new { File = file, query.SongId }, ex);
            return LyricsProviderResult.Transient;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AmllTtmlLyricsProviderTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/Services/Lyrics/AmllTtmlLyricsProvider.cs Hypersonic.Tests/Services/Lyrics/AmllTtmlLyricsProviderTests.cs
git commit -m "feat(lyrics): AMLL TTML DB provider (word-tier)"
```

---

### Task 5: Fan-out — remove Word early-exit + add `ttml` extension case (WinUI)

Make cross-provider priority authoritative on every tie, and give cached TTML a `.ttml` extension.

**Files:**
- Modify: `Hypersonic/Services/LyricsService.cs` (remove the Word early-exit block in `RunFanOutAsync`; add a `ttml` case to `ExtensionForFormat` and make it `internal`)
- Test: `Hypersonic.Tests/Services/Lyrics/LyricsServiceTests.cs` (add two tests to the existing class)

**Interfaces:**
- Consumes: existing `LyricsService`, `FakeLyricsProvider` (has settable `Id` + `Gate`), `TtmlLyricsConstants.Format`, `LyricsDocument`/`LyricsLine`/`LyricsWord`.
- Produces: no new public surface. `internal static string ExtensionForFormat(string formatTag)`.

- [ ] **Step 1: Write the failing tests**

Add to `LyricsServiceTests` (the `using`s for `TtmlLyricsConstants` and the model types are already imported via `Hypersonic.Core.Services.Lyrics` / `Hypersonic.Core.Models.Lyrics`):

```csharp
private static LyricsProviderResult WordContent(string marker)
{
    var payload = Encoding.UTF8.GetBytes(marker);
    var doc = new LyricsDocument
    {
        SyncKind = LyricsSyncKind.Word,
        SourceProvider = marker,
        Lines =
        [
            new LyricsLine
            {
                StartMs = 0, EndMs = 1000, Text = marker,
                Words = [new LyricsWord { StartMs = 0, EndMs = 1000, Text = marker }],
            },
        ],
    };
    return LyricsProviderResult.Content(doc, payload, TtmlLyricsConstants.Format, definitive: true);
}

[TestMethod]
public async Task TwoWordProviders_RegistrationOrderWins_NotArrivalOrder()
{
    // The later-registered provider returns its Word result first; the earlier one is gated (slower).
    // With the Word early-exit removed, the fan-out waits for all providers and the policy picks the
    // earliest-registered on the tie — "first", not the faster "second".
    var gate = new TaskCompletionSource<bool>();
    var first = new FakeLyricsProvider(_ => WordContent("first")) { Id = "amll-a", Gate = gate };
    var second = new FakeLyricsProvider(_ => WordContent("second")) { Id = "amll-b" };
    var service = CreateService(null, first, second);

    var task = service.GetLyricsAsync(Query);
    gate.SetResult(true); // release the slower first provider so all-complete is reached
    var doc = await task;

    Assert.AreEqual("first", doc!.SourceProvider);
}

[TestMethod]
public void ExtensionForFormat_Ttml_MapsToTtml()
    => Assert.AreEqual(".ttml", LyricsService.ExtensionForFormat("ttml"));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsServiceTests"`
Expected: FAIL — `ExtensionForFormat` is not accessible (currently `private`); the fan-out test may also fail/regress on the current early-exit.

- [ ] **Step 3: Remove the Word early-exit block in `RunFanOutAsync`**

In `Hypersonic/Services/LyricsService.cs`, inside the local `RunOne` function, delete the Word early-exit so only the all-complete early-exit remains:

```csharp
            if (result is not null)
            {
                Volatile.Write(ref slots[idx], result);
            }

            if (Interlocked.Decrement(ref remaining) == 0)
            {
                earlyExit.TrySetResult(); // all providers complete
            }
```

(The removed lines were the `if (result.Kind == LyricsOutcomeKind.Content && result.Document!.SyncKind == LyricsSyncKind.Word) { earlyExit.TrySetResult(); }` block.) Also update the `RunFanOutAsync` doc comment: remove the "OR a top-tier (Word) result arrives" clause so it reads that the fan-out decides when all providers complete or the budget elapses.

- [ ] **Step 4: Add the `ttml` case and widen `ExtensionForFormat` visibility**

In the same file, change the signature from `private static string ExtensionForFormat` to `internal static string ExtensionForFormat` and add the `ttml` case:

```csharp
    internal static string ExtensionForFormat(string formatTag) => formatTag switch
    {
        SubsonicLyricsConstants.StructuredFormat => ".json",
        SubsonicLyricsConstants.LegacyFormat => ".txt",
        LrcLibLyricsConstants.SyncedFormat => ".lrc",
        LrcLibLyricsConstants.PlainFormat => ".txt",
        TtmlLyricsConstants.Format => ".ttml",
        _ => ".txt",
    };
```

`TtmlLyricsConstants` lives in `Hypersonic.Core.Services.Lyrics` — the same namespace as `LrcLibLyricsConstants`, which `ExtensionForFormat` already uses — so `using Hypersonic.Core.Services.Lyrics;` is already present at the top of `LyricsService.cs` (around `LyricsService.cs:7`) and no new using is needed.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~LyricsServiceTests"`
Expected: PASS — the new tests pass and all pre-existing `LyricsServiceTests` still pass (no regression from removing the early-exit).

- [ ] **Step 6: Commit**

```bash
git add Hypersonic/Services/LyricsService.cs Hypersonic.Tests/Services/Lyrics/LyricsServiceTests.cs
git commit -m "refactor(lyrics): registration order decides Word ties; cache TTML as .ttml"
```

---

### Task 6: DI wiring + startup warm (WinUI)

Register the provider (last), the index, the options, and the named client; warm the index on the existing fire-and-forget lyrics seam.

**Files:**
- Modify: `Hypersonic/App.xaml.cs` (provider/index/options/HttpClient registration; add warm to `ReconcileLyricsCacheAsync`)

**Interfaces:**
- Consumes: everything from Tasks 1–4. No new tests — verified by a full build + the whole suite.

- [ ] **Step 1: Register the index, provider, options, and named client**

In `App.xaml.cs`, after the existing `services.AddSingleton<ILyricsProvider, LrcLibLyricsProvider>();` line, add the AMLL provider **last** and the index:

```csharp
            services.AddSingleton<ILyricsProvider, AmllTtmlLyricsProvider>(); // LAST: word-tier, wins only on strictly-higher tier
            services.AddSingleton<IAmllLyricsIndex, AmllLyricsIndex>();
            services.Configure<AmllLyricsOptions>(_ => { }); // defaults (Enabled=true, 7-day refresh); seam for the future toggle
```

Then, beside the existing `AddHttpClient("lrclib", …)`, register the `"amll"` client:

```csharp
            services.AddHttpClient(AmllLyricsConstants.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(AmllLyricsConstants.CdnBase);
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"Hypersonic v{lrclibVersion} (https://github.com/ningw42/Hypersonic)");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
```

Add the required usings at the top of `App.xaml.cs` if not already present: `using Hypersonic.Services.Lyrics;` and `using Hypersonic.Models;` (verify — the file already registers many `Hypersonic.Services`/`Hypersonic.Models` types, so these namespaces are likely imported).

- [ ] **Step 2: Warm the index on the existing lyrics startup seam**

In `ReconcileLyricsCacheAsync()` (`App.xaml.cs`), warm the index alongside the reconcile — both fire-and-forget, both inside the existing `try`/`catch` so a failure only logs:

```csharp
    private static async Task ReconcileLyricsCacheAsync()
    {
        try
        {
            await App.GetService<ILyricsService>().ReconcileAsync();
            await App.GetService<IAmllLyricsIndex>().WarmAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.GetService<ILoggingService>()
                .GetLogger(nameof(App))
                .Warning("Lyrics cache reconciliation failed", exception: ex);
        }
    }
```

(This method is already dispatched with `_ = ReconcileLyricsCacheAsync();` in `OnLaunched`, so warming stays off the UI-critical path.)

- [ ] **Step 3: Build the full solution to verify wiring**

Run: `dotnet build Hypersonic.sln -p:Platform=x64`
Expected: Build succeeds with no errors (DI registrations and usings resolve).

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj` then `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: All tests pass (new AMLL tests + all pre-existing tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/App.xaml.cs
git commit -m "feat(lyrics): register AMLL provider + index and warm on startup"
```

---

## Notes for the implementer

- **Verify the removed early-exit lines by content, not line number** — `RunFanOutAsync` is around `LyricsService.cs:299-329`; find the `RunOne` local function and remove only the `Word`-content `earlyExit.TrySetResult()` branch, keeping the `Interlocked.Decrement(...) == 0` branch.
- **The TTML parser is reused as-is** — do not add a new parser or format tag. `TtmlLyricsParser` is already registered in `App.xaml.cs` and returns `LyricsSyncKind.Word`.
- **`Enabled=false` returns `DefinitiveEmpty`, not `Transient`** — a permanent `Transient` would block the negative cache for every track; `DefinitiveEmpty` keeps a disabled provider inert *and* non-blocking. This differs deliberately from the warming (`Transient`) case.
- **After a real end-to-end check**, consider the `verify` skill to drive the app once (play an uncached track that exists in amll-ttml-db) and confirm word-synced lyrics appear — the unit tests cover logic, not the live CDN.
