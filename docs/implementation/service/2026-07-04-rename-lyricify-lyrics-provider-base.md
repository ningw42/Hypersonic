# Rename `CnLyricsProviderBase` → `LyricifyLyricsProviderBase` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the shared lyrics base class (and its dedicated test class + one test method) from a `Cn`-prefixed name to a `Lyricify`-prefixed name that describes the mechanism, with zero behavior change.

**Architecture:** A pure `internal` rename inside `Hypersonic/Services/Lyrics/` and its test folder. No namespace, signature, or behavior changes. The C# compiler catches every reference; the existing MSTest suite is the safety net. Files are renamed with `git mv` to preserve history; the SDK-style projects glob `**/*.cs`, so no `.csproj` edits are needed.

**Tech Stack:** .NET 10, WinUI 3 (`Hypersonic` targets `net10.0-windows10.0.19041.0`), MSTest. Build/test require `-p:Platform=x64`.

## Global Constraints

- Namespaces are **unchanged**: `Hypersonic.Services.Lyrics` (production), `Hypersonic.Tests.Services.Lyrics` (tests).
- Behavior is **unchanged** — this is a rename only. No new tests, no logic edits.
- Exact new names (verbatim): class `LyricifyLyricsProviderBase`; test class `LyricifyLyricsProviderTests`; test method `LyricifyProvider_ResolveInQQNeteaseKugouOrder` (note `QQ` double-capital, matching `QQMusicLyricsProvider`).
- **Leave untouched**: all "CN trio / CN subset / CN word parsers" comments in `App.xaml.cs` and `LyricsProviderRegistrationTests.cs`; the base class's "three search-based CN providers" doc-comment phrase; the historical doc `docs/implementation/service/2026-07-04-qq-netease-kugou-lyrics-providers.md`.
- Verification for every task: `dotnet build Hypersonic.sln -p:Platform=x64` then `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`.

---

### Task 1: Rename the production base class

Renames the base class file + type + constructor, and repoints the three subclasses. The test files reference the concrete subclasses (`QQMusicLyricsProvider`) and interfaces — not the base type by name — so the suite stays green after this task alone.

**Files:**
- Rename: `Hypersonic/Services/Lyrics/CnLyricsProviderBase.cs` → `Hypersonic/Services/Lyrics/LyricifyLyricsProviderBase.cs`
- Modify: `Hypersonic/Services/Lyrics/QQMusicLyricsProvider.cs:11`
- Modify: `Hypersonic/Services/Lyrics/NeteaseLyricsProvider.cs:11`
- Modify: `Hypersonic/Services/Lyrics/KugouLyricsProvider.cs:11`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: base type `LyricifyLyricsProviderBase` (abstract, `internal`), in namespace `Hypersonic.Services.Lyrics`, with unchanged constructor parameter list `(ILyricifyLyricsClient client, ILyricsParserRegistry parserRegistry, ILoggingService logService)` and unchanged abstract members `Id`, `Platform`, `Enabled`, `MatchThreshold`. Task 2 does not depend on these names (its tests reference the subclasses), but a reviewer should know the base kept its shape.

- [ ] **Step 1: Rename the file with git (preserves history)**

Run:
```bash
git mv Hypersonic/Services/Lyrics/CnLyricsProviderBase.cs Hypersonic/Services/Lyrics/LyricifyLyricsProviderBase.cs
```

- [ ] **Step 2: Rename the class and constructor inside the renamed file**

In `Hypersonic/Services/Lyrics/LyricifyLyricsProviderBase.cs`, change the class declaration (was line 15):

```csharp
internal abstract class LyricifyLyricsProviderBase : ILyricsProvider
```

and the constructor (was line 21) — only the identifier changes, the parameter list is untouched:

```csharp
    protected LyricifyLyricsProviderBase(ILyricifyLyricsClient client, ILyricsParserRegistry parserRegistry, ILoggingService logService)
```

Leave the `<summary>` doc-comment (including the phrase "the three search-based CN providers") exactly as-is.

- [ ] **Step 3: Repoint the three subclass base clauses**

In `Hypersonic/Services/Lyrics/QQMusicLyricsProvider.cs` (line 11):

```csharp
internal sealed class QQMusicLyricsProvider : LyricifyLyricsProviderBase
```

In `Hypersonic/Services/Lyrics/NeteaseLyricsProvider.cs` (line 11):

```csharp
internal sealed class NeteaseLyricsProvider : LyricifyLyricsProviderBase
```

In `Hypersonic/Services/Lyrics/KugouLyricsProvider.cs` (line 11):

```csharp
internal sealed class KugouLyricsProvider : LyricifyLyricsProviderBase
```

- [ ] **Step 4: Build to verify the rename compiles**

Run:
```bash
dotnet build Hypersonic.sln -p:Platform=x64
```
Expected: `Build succeeded.` with `0 Error(s)`. (A lingering reference to `CnLyricsProviderBase` would surface here as CS0246 "type or namespace not found".)

- [ ] **Step 5: Run the app test suite to verify behavior is unchanged**

Run:
```bash
dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64
```
Expected: `Passed!` — all tests pass, including `CnLyricsProviderTests` (still under its old name at this point) and `LyricsProviderRegistrationTests.CnProviders_ResolveInQqNeteaseKugouOrder`.

- [ ] **Step 6: Commit**

```bash
git add Hypersonic/Services/Lyrics/LyricifyLyricsProviderBase.cs Hypersonic/Services/Lyrics/QQMusicLyricsProvider.cs Hypersonic/Services/Lyrics/NeteaseLyricsProvider.cs Hypersonic/Services/Lyrics/KugouLyricsProvider.cs
git commit -m "refactor(lyrics): rename CnLyricsProviderBase to LyricifyLyricsProviderBase"
```

---

### Task 2: Rename the test class and test method

Aligns the test surface with the production rename: the dedicated base-behavior test class and the one registration test method that named the "Cn" grouping.

**Files:**
- Rename: `Hypersonic.Tests/Services/Lyrics/CnLyricsProviderTests.cs` → `Hypersonic.Tests/Services/Lyrics/LyricifyLyricsProviderTests.cs`
- Modify: `Hypersonic.Tests/Services/Lyrics/LyricsProviderRegistrationTests.cs:47`

**Interfaces:**
- Consumes: the renamed production types from Task 1 (already built and green). The test bodies reference `QQMusicLyricsProvider`, `ILyricifyLyricsClient`, `LyricifyLyricsResult`, `LyricifyPlatform` — none of which change in this task.
- Produces: nothing consumed by later tasks (final task).

- [ ] **Step 1: Rename the test file with git**

Run:
```bash
git mv Hypersonic.Tests/Services/Lyrics/CnLyricsProviderTests.cs Hypersonic.Tests/Services/Lyrics/LyricifyLyricsProviderTests.cs
```

- [ ] **Step 2: Rename the test class inside the renamed file**

In `Hypersonic.Tests/Services/Lyrics/LyricifyLyricsProviderTests.cs`, change the class declaration (was line 16); the `[TestClass]` attribute above it stays:

```csharp
public class LyricifyLyricsProviderTests
```

Leave every test method inside (`ProviderId_IsQq`, `Disabled_ReturnsDefinitiveEmpty`, etc.) unchanged.

- [ ] **Step 3: Rename the registration test method**

In `Hypersonic.Tests/Services/Lyrics/LyricsProviderRegistrationTests.cs`, change the method declaration (line 47); the `[TestMethod]` attribute and the `NOTE` comment above it stay untouched:

```csharp
    public void LyricifyProvider_ResolveInQQNeteaseKugouOrder()
```

Do not alter the method body or the "CN trio / CN subset" comments.

- [ ] **Step 4: Build to verify compilation**

Run:
```bash
dotnet build Hypersonic.sln -p:Platform=x64
```
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 5: Run the suite and confirm the renamed tests execute**

Run:
```bash
dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64
```
Expected: `Passed!` — the renamed `LyricifyProvider_ResolveInQQNeteaseKugouOrder` and the `LyricifyLyricsProviderTests` cases run and pass; no `CnLyricsProviderTests` / `CnProviders_...` names remain.

- [ ] **Step 6: Commit**

```bash
git add Hypersonic.Tests/Services/Lyrics/LyricifyLyricsProviderTests.cs Hypersonic.Tests/Services/Lyrics/LyricsProviderRegistrationTests.cs
git commit -m "refactor(lyrics): rename CN lyric provider tests to Lyricify"
```

---

## Final verification

- [ ] **No stray occurrences of the old names remain in code**

Run (scoped to source files):
```bash
git grep -n "CnLyricsProviderBase\|CnLyricsProviderTests\|CnProviders_ResolveInQqNeteaseKugouOrder" -- '*.cs'
```
Expected: **no output** (grep exits non-zero) — the old names survive only in documentation prose (the historical implementation doc, plus this rename's own design/plan docs, which intentionally describe the change), never in code.
