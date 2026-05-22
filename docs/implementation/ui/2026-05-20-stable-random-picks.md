# Stable Random Picks — Implementation Notes

Companion to [`docs/design/ui/2026-05-20-stable-random-picks.md`](../../design/ui/2026-05-20-stable-random-picks.md). This document covers implementation-specific decisions, deviations from the design, and details that only appear in source.

---

## Service location: `Hypersonic`, not `Hypersonic.Core`

The design doc is ambiguous about which assembly the cache service lives in. `Hypersonic.Core` is the platform-independent project and does **not** reference `Hypersonic.Client`, so a service that calls `ISubsonicClient.GetAlbumList2Async` cannot live there. The cache was placed in `Hypersonic/Services/` alongside `ScrobblingService`, `SubsonicClientService`, and other services that depend on the Subsonic API client. The contract lives at `Hypersonic/Contracts/Services/IRandomPicksCacheService.cs`, matching the WinUI-side service convention.

---

## Single-flight: in-flight `Task` is the gate, not a `Loaded` flag

The design doc requires single-flight semantics with an in-flight `Task<IReadOnlyList<AlbumInfo>>` that subsequent callers await. The implementation does this, but the first attempt hit a subtle race that's worth documenting:

The original draft cleared `_inflight = null` inside `FetchAsync`'s finally block, and `GetOrFetchAsync` used `_inflight is null` as the "no fetch running" check. This worked in production (real HTTP requests always have at least one true `await` that suspends) but failed in tests where the fake `ISubsonicClient` returned a synchronously-completed task. The sequence:

1. `GetOrFetchAsync` enters the lock and assigns `_inflight = FetchAsync()`.
2. `FetchAsync` runs synchronously through every `await` (all already-completed tasks), reaches its finally block, sets `_inflight = null`, and returns a completed Task.
3. Control returns to step 1's assignment, which now overwrites the just-cleared `_inflight` with the (completed) returned Task.
4. The next `RefreshAsync` sees `_inflight is not null` and joins the already-completed task instead of issuing a new fetch.

The fix: `_inflight` is never explicitly cleared. Instead, `IsTaskRunning(Task?)` and `IsTaskDone(Task?)` helpers test `task is null || task.IsCompleted`. A completed task is treated as "no fetch running," so a refresh request after completion correctly issues a new fetch.

This is functionally equivalent to clearing `_inflight` but immune to the synchronous-completion race. Worth keeping in mind if anyone changes this code.

---

## Refresh is "no-op while in flight," not "coalesce-and-fire-again"

The design doc allows either no-op or coalesce semantics for refresh-while-in-flight as long as it's documented. The implementation chose **no-op (join the existing call)**: if a fetch is already in flight, `RefreshAsync` returns the same task and does not queue a follow-up refresh.

Rationale: the alternative (queue a second refresh after the first completes) would mean a user spamming the shuffle button could create a chain of pending refreshes, defeating single-flight. No-op is simpler and matches the user expectation that "I clicked while it was already refreshing — that's the same as clicking once."

---

## `RetryAsync` applies the cache's current picks explicitly

The design doc says the page-level retry "must also ask the cache service to retry" — either via a clear-and-read or via the same refresh entry point. The implementation calls `_randomPicksCache.RefreshAsync()`. After the refresh completes, `RetryAsync` also calls `ApplyRandomPicks(_randomPicksCache.CurrentPicks)` directly.

Why the explicit apply call when the `PicksChanged` subscription would normally handle it: the subscription is set up in `OnNavigatedTo` via `Subscribe()`. In tests (and conceivably in obscure timing edge cases) `RetryCommand` can run on a VM that hasn't yet wired up the subscription. The explicit `ApplyRandomPicks` is idempotent — if the subscription already ran, calling it again with the same list is harmless. Belt and suspenders.

---

## Threading: dispatcher captured in VM constructor, optional in tests

The cache service raises `PicksChanged` and `LoadingChanged` from whatever thread completes the fetch (typically a thread-pool continuation). The VM marshals back via a captured `DispatcherQueue`:

- Public constructor: `_vmDispatcher = DispatcherQueue.GetForCurrentThread()` and `_enqueue = action => _vmDispatcher.TryEnqueue(...)`.
- `internal` constructor: takes an `Action<Action> enqueue` parameter so tests can pass `action => action()` for synchronous, on-thread execution.

If `TryEnqueue` returns `false` (no dispatcher available, e.g. headless test contexts), the production `_enqueue` falls back to running the action inline. This avoids a hard dependency on dispatcher availability while still preferring marshalled execution when a dispatcher exists.

This mirrors the pattern already used by `NowPlayingViewModel`.

---

## `LoadAllAsync` extracted for test-friendly orchestration

`OnNavigatedTo` is `async void`, which makes it awkward to await from tests. The implementation adds an `internal Task LoadAllAsync()` that performs the same work as `OnNavigatedTo` minus the `Subscribe()` call. Tests call `LoadAllAsync` directly; tests that need the subscription wiring (e.g. `PicksChangedEvent_UpdatesRandomPicks_AndAggregateState`) call `OnNavigatedTo` and `await Task.Yield()` to flush the async-void continuation.

---

## UI: button placement and visibility model deviate from the design

The design doc specifies the refresh affordance as:
- An icon-only `Button` using the "Refresh" Segoe Fluent glyph
- Right-aligned in the header row
- Always visible at section opacity (matching the section header's visual weight)

The implementation deviates on placement and resting visibility based on user feedback during implementation:

- **Position**: next to the title (`Random Picks [↻]`), not right-aligned. Reason: right-alignment felt visually disconnected from the title in long carousel rows.
- **Style**: circular bordered button (`32×32`, `CornerRadius=16`, `BorderThickness=1`), not the plain `SubtleButtonStyle` originally drafted. Reason: more clearly affords "click me" for the shuffle action.
- **Resting visibility**: hidden (`Opacity=0`, `IsHitTestVisible=false`), revealed on pointer-enter of the section with a 150ms `ScalarTransition`. Reason: an always-visible button next to the title competed for attention with the title itself; hover-reveal keeps the header clean while still surfacing the affordance when the user is interacting with the section.

Loading-state visuals match the design (glyph ↔ `ProgressRing` swap inside a fixed-size `Grid`, no layout shift).

---

## `HasLoaded` flips only on success — failed first fetch is retried

The cache's `HasLoaded` flag is set `true` only inside the success branch of `FetchAsync`. A failed first attempt leaves the cache in "not loaded" state, so the next `GetOrFetchAsync` (typically the next navigation to the home page) reissues the fetch. This matches the design doc's "next read will retry" promise.

An earlier draft set `_hasLoaded = true` unconditionally inside the finally block, reasoning that "we did try, that counts as loaded." That was wrong: combined with `PrimeRandomPicksAsync`'s `if (HasLoaded && !IsLoading)` fast path, one transient first-load failure would have hidden Random Picks for the entire process lifetime (until a page-level retry, which doesn't fire on a happy navigation). The `RandomPicksCacheServiceTests` includes a `GetOrFetchAsync_AfterFirstFailure_RetriesOnNextCall` regression test that locks this in.

Null-client follows the same rule: no client means no fetch was attempted, so `HasLoaded` stays `false` and the next read tries again (presumably once a client has been provisioned).

---

## Hover-reveal: `PointerExited` plus a `PointerMoved` backstop, plus focus reveal for keyboard

`PointerEntered`/`PointerExited` on the section's outer `StackPanel` (with `Background="Transparent"` so empty space participates in hit-testing) is the canonical pattern for hover-reveal UI in WinUI. It works for fast pointer movements but breaks when the pointer crosses children that handle pointer events themselves — in this section, the `RichTextBlock` artist text in the metadata panel (with hyperlinks and `IsTextSelectionEnabled`) can swallow the bubbled `PointerExited` even when the pointer has actually left the section.

The implementation adds a `PointerMoved` handler on the page's outer `ContentArea` Grid as a backstop: on every page-level pointer move, if the shuffle button is currently visible, re-test the pointer position against the section bounds and hide if outside. An early-out (`if (!_shufflePointerInside) return;`) keeps the per-move cost negligible — the bounds check runs only while the button is shown.

This is a recognized WinUI pattern for hover-reveal that needs to be reliable across children with non-trivial hit-test behaviour. It's not strictly canonical (the canonical approach would be a custom `Control` with `VisualStateManager` pointer states), but it is the right tradeoff for one-off hover-reveal on a `StackPanel`.

### Keyboard accessibility

`Opacity="0"` and `IsHitTestVisible="False"` hide the button from sight and mouse, but neither removes it from the keyboard tab order — without intervention, Tab navigation would land on an invisible focusable button. To fix this without removing keyboard access, the visibility model is now "reveal on hover **or** focus":

- Two private flags track state: `_shufflePointerInside` (set by `PointerEntered`/`PointerExited`/`PointerMoved`) and `_shuffleHasFocus` (set by the button's `GotFocus`/`LostFocus`).
- `UpdateShuffleButtonVisibility()` sets `Opacity` and `IsHitTestVisible` based on `_shufflePointerInside || _shuffleHasFocus`.
- A keyboard user Tab-ing through the page sees the button fade in when it receives focus, and fade out again when focus moves on.
- The button is never removed from the tab order, so keyboard discoverability is preserved.

---

## Tooltip localization deferred

The design doc specifies a "Shuffle" tooltip. The repository has no precedent for localizing attached properties via `x:Uid` (no existing `*.[using:…]ToolTipService.ToolTip` entries in `Resources.resw`), and the app currently ships only `en-us`. The tooltip is hard-coded inline as `ToolTipService.ToolTip="Shuffle"`. Easy to lift to `Resources.resw` when a second locale is added.

---

## Failure-count denominator is a constant, not a magic number

The design doc flags that the failure-count check must move from `5` to `4` (or "better, compare to the number of tasks actually launched"). The implementation defines `private const int NonRandomSectionCount = 4;` and passes it to `RecomputeAggregateState(failureCount, NonRandomSectionCount)`. If a new non-random section is ever added to `LoadSectionsAsync`, the constant is the single point of change.

`RecomputeAggregateState` itself is factored out so both `LoadSectionsAsync` and `ApplyRandomPicks` can update aggregate state consistently — `ApplyRandomPicks` doesn't pass a failure count (it never causes the page-level error state to flip on, only off), so it inlines the `HasAnyContent` recompute and the `HasError &&= !HasAnyContent` clear.

---

## Test fakes: real cache for most tests, `FakeRandomPicksCache` for VM-cache interactions

Most `MainViewModelTests` instantiate the real `RandomPicksCacheService` and exercise the full path through `ISubsonicClient`. This keeps tests close to production behavior and verifies the cache and VM cooperate correctly.

A `FakeRandomPicksCache` is used only for the few tests that verify VM-side behavior independent of the cache implementation:
- `RefreshRandomPicksCommand_DelegatesToCache` — needs to count calls into the cache without exercising the real fetch path.
- `PicksChangedEvent_UpdatesRandomPicks_AndAggregateState` — needs to push picks at a specific moment to test the event handler.
- `OnNavigatedFrom_Unsubscribes_FromCacheEvents` — needs to push picks after unsubscribe to verify no leak.

The fake exposes a `PushPicks(IReadOnlyList<AlbumInfo>)` test seam that raises `PicksChanged` directly, simulating a refresh completing.

---

## DI registration order

`IRandomPicksCacheService` is registered after `IAppStateService` in `App.xaml.cs`, immediately before the section that registers view models. It's a singleton with two dependencies (`ISubsonicClientService`, `ILoggingService`), both already registered as singletons earlier in the same `ConfigureServices` call.

No registration ordering concern: Microsoft.Extensions.DependencyInjection resolves singletons lazily by type, not by registration order.

---

## Files touched

| File | Change |
|---|---|
| `Hypersonic/Contracts/Services/IRandomPicksCacheService.cs` | New — interface |
| `Hypersonic/Services/RandomPicksCacheService.cs` | New — implementation |
| `Hypersonic/App.xaml.cs` | Register `IRandomPicksCacheService` as singleton |
| `Hypersonic/ViewModels/MainViewModel.cs` | Consume cache; add `RefreshRandomPicksCommand`, `IsRefreshingRandomPicks`, `ApplyRandomPicks`, `RecomputeAggregateState`, `LoadAllAsync`, `PrimeRandomPicksAsync`, dispatcher marshaling, subscribe/unsubscribe lifecycle; `RetryAsync` refreshes cache |
| `Hypersonic/Views/MainPage.xaml` | Shuffle button next to title; `Background="Transparent"` on `RandomPicksSection`; `PointerEntered`/`PointerExited` hooks; `PointerMoved` on `ContentArea` |
| `Hypersonic/Views/MainPage.xaml.cs` | Pointer handlers, bounds-check helper, show/hide helper |
| `Hypersonic.Tests/Services/RandomPicksCacheServiceTests.cs` | New — 9 cache tests |
| `Hypersonic.Tests/ViewModels/MainViewModelTests.cs` | Updated constructor; added `FakeRandomPicksCache`; added retry/refresh/subscribe/stability tests |
