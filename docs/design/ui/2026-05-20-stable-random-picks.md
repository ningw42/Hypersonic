# Stable Random Picks — Per-Run Persistence & On-Demand Refresh

## Overview

The home page's **Random Picks** carousel currently reshuffles every time `MainPage` is navigated to, because `MainViewModel` is registered transient and `OnNavigatedTo` unconditionally calls `LoadSectionsAsync()`, which in turn fires `GetAlbumList2Async("random", size=20)`. Subsonic's `random` list type is non-deterministic and the spec exposes no seed parameter, so identical calls return different results.

This design makes the Random Picks selection **stable for the lifetime of a single app run** and gives the user **explicit control** over when to reshuffle, via a refresh affordance on the carousel header. Other home sections (Recently Played, Recently Released, Recently Added, Most Played) continue to refresh on every navigation — only the random section is cached.

The picks intentionally do **not** persist across process restarts. The "per run" lifetime keeps the surface area small: no `AppState.json` schema changes, no invalidation logic for albums that have been deleted or renamed on the server, no first-launch-after-update edge cases. A restart is the natural reshuffle.

## Goals

- Random Picks remain identical across navigations within one process lifetime.
- The user can reshuffle on demand without leaving the home page.
- Other home sections are unaffected — they still reflect server state on each visit.
- No new persistence schema; the cache is in memory only.

## Non-goals

- Cross-restart stability. Deferred; revisit only if user feedback asks for it.
- Server-seeded randomness (would require an OpenSubsonic extension).
- Reshuffling on any signal other than explicit user action (e.g., library change events, time-based expiry). The cache invalidates only on app exit or refresh click.

## Approach

### Cache scope — singleton, in-memory

A new singleton service holds the cached random-picks list for the app's lifetime. `MainViewModel` consults this service instead of calling `GetAlbumList2Async("random", …)` directly. The service is registered alongside the other singletons in `App.xaml.cs`.

A singleton is the right scope because:
- `MainViewModel` is transient — caching inside the VM dies on every navigation away.
- `IAppStateService` is for *persisted* runtime state. Random Picks are explicitly **not** persisted, so reusing that contract would muddy its purpose.
- A dedicated, single-responsibility service makes the lifetime intent obvious from the type name.

The service holds the list and a `Loaded` flag. First read triggers a fetch; subsequent reads return the cached list. A `Refresh` method clears the cache and refetches on the next read (or eagerly — see [Refresh semantics](#refresh-semantics)).

The service surface stays minimal:

- A read entry point that returns the current picks (fetching on first call).
- A refresh entry point that invalidates and refetches.
- An `IsLoading` / state signal so the UI can show a spinner during refresh.
- A `Changed` event (or observable property) so `MainViewModel` can update its `RandomPicks` collection when a refresh completes.

**Single-flight semantics are mandatory.** The service must serialize concurrent first-load and refresh attempts: at most one outstanding `GetAlbumList2Async("random", …)` call can be in flight at any time, and concurrent callers must observe the result of that single call rather than each triggering their own fetch. A bare `Loaded` flag is insufficient — two rapid navigations or a navigate-then-refresh sequence before the first response returns would otherwise issue parallel calls, and the last response to land would clobber the first, violating the "stable for one run" promise (the user could see picks A briefly, then picks B settle in). The implementation must hold an in-flight `Task<IReadOnlyList<AlbumInfo>>` (or equivalent) that subsequent callers await. Refresh, while in flight, must either be a no-op or coalesce with the existing call — pick one and document it; do not allow overlap.

### Read path — MainViewModel becomes a consumer, not a fetcher

`MainViewModel.LoadSectionsAsync` currently kicks off five parallel fetches. The random fetch is removed from that path. Instead, `MainViewModel`:

1. Subscribes to the cache service's change event in its constructor.
2. On `OnNavigatedTo`, asks the service for its current picks. If the service has them, populate `RandomPicks` synchronously. If not, the service starts an async fetch and the event handler will populate `RandomPicks` when it completes.
3. On `OnNavigatedFrom`, unsubscribes to avoid leaks (transient VM, singleton service — classic leak shape).

The other four sections retain their current per-navigation refresh behavior.

#### Aggregate state must be recomputed when random arrives asynchronously

Today, `LoadSectionsAsync` populates all five collections in one pass and then computes `HasAnyContent` and `HasError` once at the end. After this change, random arrives on a separate channel (the cache service's change event), so the aggregate state computation can no longer live exclusively at the end of `LoadSectionsAsync`. Two consequences:

- **The failure-count denominator changes from 5 to 4.** `HasError` currently fires when `failureCount == 5`. After the split, `LoadSectionsAsync` orchestrates only four fetches; the check must become `failureCount == 4` (or, better, compare to the number of tasks actually launched). Leaving the literal `5` would mean four failed non-random sections would silently show the empty state instead of the error state.
- **The random change-event handler must update aggregate state.** When the cache service signals a new list, the handler must (a) replace `RandomPicks`, (b) raise `HasRandomPicks`, (c) recompute and raise `HasAnyContent`, and (d) clear `HasError` if it is currently true and random is now non-empty (the page-level error UI should not remain visible once any content lands). Without this, the `ScrollViewer` — gated on `HasAnyContent` (see [`MainPage.xaml`](../../../Hypersonic/Views/MainPage.xaml)) — stays hidden when random is the only successful section, e.g. on a brand-new server with no scrobble data and no year tags. The cleanest implementation is to factor the aggregate-state update into a small private method that both `LoadSectionsAsync` and the random handler call.

### Refresh semantics

A refresh button on the **Random Picks** section header invokes a `RefreshRandomPicksCommand` on `MainViewModel`, which calls the cache service's refresh method. The service:

1. Sets an `IsLoading` flag (the UI can disable the button and overlay a spinner on the coverflow).
2. Issues a fresh `GetAlbumList2Async("random", size=20)` call.
3. On success: replaces the cached list and raises the change event.
4. On failure: keeps the previous list (don't blank the UI on a transient error), surfaces the error via `ILogger`, clears `IsLoading`. A future iteration could add a teaser/toast; for v1 the silent retain + log is acceptable because the user can simply click again.

The refresh button is **disabled while a refresh is in flight** and while the initial load hasn't completed. If the section is hidden (zero results from the server), the button is naturally not visible.

### UI — refresh button placement

The Random Picks section already has a `TextBlock` header. The refresh affordance lives in a small `Grid` row alongside the title:

```
┌───────────────────────────────────────────────────────────┐
│  Random Picks                                       [ ↻ ] │
│  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ...                 │
│  │      │ │      │ │      │ │      │                     │
│  └──────┘ └──────┘ └──────┘ └──────┘                     │
└───────────────────────────────────────────────────────────┘
```

Button style: an unobtrusive icon-only `Button` using the Segoe Fluent Icons "Refresh" glyph (``), matching the visual weight of the section header. Hover reveals a tooltip ("Shuffle"). The button is right-aligned in the header row so it doesn't compete with the title.

While `IsLoading` is true, the glyph is replaced with a small `ProgressRing` or the button shows a spinner overlay — pick the lighter option that the existing app patterns already use. The button itself remains in place (no layout shift).

### What stays the same

- The coverflow control, item template, `SelectedIndex="9"` centering, hover/press transforms.
- The five-section vertical layout on `MainPage`.
- The non-random sections' load behavior.
- `GetAlbumList2Async` signature and `AlbumMapper`.

## Key Decisions

### Why a dedicated service rather than caching inside `MainViewModel`?

`MainViewModel` is transient — every navigation to `MainPage` constructs a new instance, so any field-level cache would be discarded immediately. Promoting `MainViewModel` to singleton was considered but rejected: the VM holds five `ObservableCollection`s and a load-state flag that should logically reset per navigation for the non-random sections, plus singleton-izing a navigable VM creates subtle issues with `INavigationAware` lifecycle assumptions. A dedicated singleton just for the random cache keeps each component's lifetime aligned with its purpose.

### Why per-run instead of persisted?

Persisting across restarts costs more than the user-visible benefit justifies:

- The cached list stores album IDs (or full `AlbumInfo`). Either way, items can disappear or rename between sessions (library rescans, deletions). A persisted cache needs invalidation logic.
- `AppState.json` would gain a schema field that exists only to make a freshness/expiry policy work.
- "Random Picks" implies *some* freshness over time. A restart is the natural, intuitive reshuffle cadence.

If users later ask for cross-restart stability, the singleton can grow a serialization hook without invalidating the rest of this design.

### Why an explicit refresh button instead of a time-based or count-based auto-refresh?

Both would surprise users. An explicit button is a single, discoverable affordance that gives the user the model in one click: *this list is stable until I or the app restart change it*. It also keeps the implementation free of timers, navigation-count tracking, and edge cases around what counts as "fresh enough."

### Account/server switching

Not relevant for the current app: there is no in-session sign-out or server-switching flow today. The cache can therefore assume a single active Subsonic client for the app run.

If Hypersonic later adds in-session account/server switching, that feature should also add a clear client/server identity-change signal. At that point, this cache must invalidate on that signal before the switching flow ships.

### Failure behavior on first load

If the initial `random` fetch fails, `RandomPicks` stays empty and `HasRandomPicks` is false, so the section hides — same as today. The cache remains in the "not loaded" state, so a future read attempt will retry the fetch.

**Page-level retry must explicitly cover random.** Today, `RetryAsync` (the page-level error-state button) just re-invokes `LoadSectionsAsync`. After this change, `LoadSectionsAsync` no longer touches random, so the page-level retry would *not* re-trigger a failed initial random fetch — leaving the user stuck with no obvious recovery path if random was the only section the server actually populates (the per-section refresh button only appears once Random Picks is visible, which it won't be on initial failure). To fix this, `RetryAsync` must also ask the cache service to retry: either by clearing the cache and triggering a read, or by calling the same refresh entry point used by the in-header shuffle button. Both options route through the single-flight gate described above, so concurrent retry-plus-navigate is safe.

### Threading

The service is touched from the UI thread (VM subscriptions, refresh command) and from completion callbacks of the API client. Use the same dispatcher discipline already established elsewhere in the codebase (the `DispatcherQueue` captured by the consuming VM, or marshal back inside the service before raising events). The exact pattern matches what `PlaybackService` already does for its public events.

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Memory leak: transient VM subscribes to singleton event, never unsubscribes | `OnNavigatedFrom` unsubscribes; weak event subscription as a belt-and-suspenders option if the pattern is otherwise error-prone. |
| Stale cache after album deletion (album shows in carousel, opens to 404) | The selection is a snapshot; opening a deleted album would already error with the current implementation on slow navigation. Acceptable risk for v1; the user can click refresh. |
| User confusion: "why doesn't Random Picks change anymore?" | The refresh button is the in-product answer. Tooltip text ("Shuffle") communicates intent. |
| First-load latency: random fetch is now serialized after navigation rather than parallel with the others | Keep the random fetch parallel with the rest on the first navigation (the cache service can be primed eagerly by `MainViewModel` on first load, *and* return its cached value on subsequent loads). |

## Testing

- **Cache service unit tests** (in `Hypersonic.Tests`, since the cache touches `ISubsonicClientService`):
  - First read triggers a fetch; second read does not.
  - Refresh triggers a fetch and raises the change event.
  - Refresh failure retains previous list and surfaces the error.
- **`MainViewModel` tests** in the existing `MainViewModelTests.cs` should be extended to:
  - Verify `LoadSectionsAsync` no longer calls the random endpoint directly.
  - Verify `RefreshRandomPicksCommand` delegates to the cache service.
  - Verify subscription/unsubscription on navigation.
- **No new UI tests** required — the refresh button is a `Button` bound to a `RelayCommand`; standard MVVM coverage suffices.

## Out of Scope / Future Work

- **Cross-restart persistence**: gate behind explicit user demand.
- **Server-side seed**: file as an OpenSubsonic spec enhancement separately.
- **In-session account/server switching invalidation**: not applicable today because the app has no sign-out or server-switching flow. Revisit with that feature.
- **Per-section refresh buttons** on the other carousels: the other sections refresh on navigation, which is the natural cadence for "recent" / "frequent" semantics. Add only if users ask.
- **Lock icon / pin gesture** to "pin" the current set across restarts: interesting but speculative.

## Related Documents

- [`docs/design/ui/2026-04-21-home-page.md`](2026-04-21-home-page.md) — Home page overall design, section contracts.
- [`docs/design/ui/2026-05-07-coverflow-control.md`](2026-05-07-coverflow-control.md) — Coverflow control design used to render Random Picks.
