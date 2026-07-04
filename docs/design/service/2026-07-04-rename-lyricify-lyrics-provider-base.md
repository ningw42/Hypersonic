# Rename `CnLyricsProviderBase` → `LyricifyLyricsProviderBase` — Design

A focused rename within the lyrics stack introduced by the QQ / Netease / Kugou providers
(`docs/design/service/2026-07-04-qq-netease-kugou-lyrics-providers.md`). No behavior change —
this realigns one name with what the type actually *does*.

## Why

The base class's shared job is *"fetch a candidate via the `LyricifyLyricsClient` facade, gate it on
`LyricsMatchScorer`, parse the raw payload through the registry."* Its identity is the **Lyricify
mechanism**, not the Chinese origin of its three subclasses. It already sits in a `Lyricify*` family —
`ILyricifyLyricsClient`, `LyricifyLyricsClient`, `LyricifyPlatform`, `LyricifyWordLyricsParser`,
`LyricifySyllableMapper`, `LyricifyLyricsResult`. Naming the base after the mechanism makes the
abstraction self-describing and consistent with its neighbors.

"CN" (Chinese) stays wherever it labels the QQ / Netease / Kugou **grouping** — a concept orthogonal to
the base's name. That distinction is the whole point of the rename: the *base* is about Lyricify; the
*set of subclasses* happens to be Chinese platforms.

## Scope

A pure `internal` rename. No namespace change, no public/API surface, no behavior change. The compiler
catches every reference, so risk is minimal and verification is a build + the existing test run.

| Target | Change |
|---|---|
| `Hypersonic/Services/Lyrics/CnLyricsProviderBase.cs` | Rename file → `LyricifyLyricsProviderBase.cs`; rename the class and its constructor |
| `QQMusicLyricsProvider.cs` | Base clause `: CnLyricsProviderBase` → `: LyricifyLyricsProviderBase` |
| `NeteaseLyricsProvider.cs` | Same base-clause change |
| `KugouLyricsProvider.cs` | Same base-clause change |
| `Hypersonic.Tests/Services/Lyrics/CnLyricsProviderTests.cs` | Rename file → `LyricifyLyricsProviderTests.cs`; rename the test class |
| `Hypersonic.Tests/Services/Lyrics/LyricsProviderRegistrationTests.cs` | Rename test method `CnProviders_ResolveInQqNeteaseKugouOrder` → `LyricifyProvider_ResolveInQQNeteaseKugouOrder` (also normalizes `Qq` → `QQ`, matching the `QQMusicLyricsProvider` double-capital convention) |

## Deliberately left untouched

- The **"CN trio / CN subset / CN word parsers"** comments in `App.xaml.cs` and
  `LyricsProviderRegistrationTests.cs` — they describe the Chinese-platform grouping, which remains
  accurate. (The `NOTE` comment above the renamed test method still reads "CN trio"; accurate and kept.)
- The base class's own doc-comment phrase "the three search-based CN providers" — same reasoning.
- The historical implementation record
  `docs/implementation/service/2026-07-04-qq-netease-kugou-lyrics-providers.md` — a dated artifact of
  what was built under the old name; left as history.

## Verification

`dotnet build Hypersonic.sln -p:Platform=x64`, then `dotnet test Hypersonic.Tests -p:Platform=x64`
(covers both the renamed provider tests and the registration test).
