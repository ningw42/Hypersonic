# Cross-Surface Favorite (Star) Synchronization — Design

Make a song's starred state a single shared fact across every surface that shows it, so favoriting in one place updates the same song everywhere — including changes made outside this client (Navidrome's web player, another device) — and collapse the five near-duplicate toggle implementations into one correct path.

This is a follow-up to the player-bar favorite button (`docs/design/ui/2026-06-07-player-bar-favorite-button.md`), whose "Scope exclusions" explicitly deferred cross-page sync.

---

## What we're building

Today star state is duplicated per surface and each surface owns its own toggle:

- Track-list rows hold `SongInfo.IsStarred` (album detail, playlist detail, artist top songs, all-tracks).
- Now-playing / queue hold `PlaybackTrack.IsStarred`.
- The player bar holds `PlayerViewModel.IsStarred` (a mirror of the now-playing track).

Five `ToggleStar*` commands each do their own optimistic flip → `Star/UnstarAsync` → revert-on-failure. Four of them (`TracksViewModel`, `AlbumDetailViewModel`, `PlaylistDetailViewModel`, `ArtistDetailViewModel.ToggleTopSongStarAsync`) **do not check `response.Status`**, so a server-rejected star (Subsonic returns HTTP 200 with `status="failed"`) silently shows as favorited. Only `PlayerViewModel` is hardened. `ArtistDetailViewModel` additionally hand-rolls a within-VM duplicate-id sync (`SyncStarInBothCaches`).

We introduce one app-layer singleton, **`FavoritesService`**, that is the single writer of an in-memory set of starred song ids. It owns the API call + status check + optimistic revert in exactly one place, and broadcasts every change as a batched delta over `WeakReferenceMessenger`. Every surface becomes a pure subscriber that updates only the model instances it owns whose id is in the message.

The store is fed by three inputs — user toggles, a background `getStarred2` poll, and page-load observations — and emits one signal. The server stays the ultimate source of truth; the store is the in-memory copy that lets us detect external changes and keep already-materialized instances coherent.

Scope is **songs only**. `StarAsync`/`UnstarAsync` already take `albumId`/`artistId`, and the message/store are id-based, so album/artist stars can be added later without redesign — but no album/artist star UI exists today, so we don't build that machinery now.

This is the codebase's **first use of `WeakReferenceMessenger`**. The plumbing already exists because every ViewModel extends `ObservableRecipient`; the messenger is injected as `IMessenger` (see DI) rather than reached statically, so tests can isolate it.

---

## Pivotal decisions

### One writer, many subscribers — surfaces never call the API directly

The core inversion: a `ToggleStar` command stops mutating its own model and instead *asks the service* to toggle. The service applies the change to its store, **broadcasts synchronously**, does the network call, and broadcasts a revert if it fails. Every surface — including the one the user clicked — updates by reacting to the broadcast.

```
                 ┌──────────────────────────────────────────────┐
  user toggle ──▶│  FavoritesService  (single writer)           │──▶ FavoritesChangedMessage
  bg poll ──────▶│    _starred : HashSet<string>     (the stars)│     (bool IsStarred, string[] Ids)
  page load ────▶│    _epoch / _seq / _lastWriteSeq   (gate)    │            │ IMessenger (injected)
                 │    _desired / _refresh            (leases)   │            ▼
                 └──────────────────────────────────────────────┘
   track-list VMs (SongInfo)   PlayerViewModel (mirror + queue PlaybackTracks)   queue rows (Track.IsStarred)
```

This is what removes the duplication: the optimistic-flip / `Star`-or-`Unstar` / status-check / revert logic exists once, in the service, instead of five times. The four missing `status=="failed"` checks are fixed for free because there is now only one check.

### The toggle is optimistic *via the broadcast*, not via the caller

`ToggleAsync(id, desired)` broadcasts `(desired, [id])` **before** awaiting the network call. Because the toggle runs on the UI thread the service [sends inline](#ui-thread-affinity-with-an-explicit-broadcast-dispatch-rule) (not via the dispatcher queue), so `WeakReferenceMessenger.Send` invokes recipients synchronously and the initiating surface's own handler runs reentrantly and updates the clicked row instantly — the click still feels immediate, with no per-VM optimistic code. On failure (thrown exception **or** `response.Status == "failed"`), the service reverts to the last confirmed server value and broadcasts that — `(!desired, [id])` in the simple single-toggle case (see [coalescing](#concurrent-toggles-for-one-song-are-serialized-and-coalesced) for how `serverKnown` generalises it) — and every surface, including the initiator, rolls back.

### The player-bar mid-flight race disappears

The player-bar design carries a bespoke `RevertStar` to handle "track changed during an in-flight toggle that then failed — don't clobber the new track's heart." With id-keyed broadcasts this is handled structurally: the revert message carries the *specific* id, and the player handler only touches its mirror when `NowPlaying.Id` is in the message. If playback advanced, the id no longer matches → the mirror is untouched, while the old track's own `PlaybackTrack.IsStarred` (still in the queue) reverts correctly. `RevertStar` is deleted.

### A batched `(bool, string[])` delta, not a central queryable set

The message is `FavoritesChangedMessage(bool IsStarred, IReadOnlyList<string> Ids)` — a delta, not a snapshot. Batching by shared boolean lets the background poll emit at most two messages per cycle (one newly-starred group, one newly-unstarred group) instead of one per id.

We deliberately do **not** make surfaces query a central `Set<string>` to render. XAML binds to per-object observable booleans (`SongInfo.IsStarred`, `PlaybackTrack.IsStarred`, the player mirror); rebinding every heart to a store lookup would require injecting the store into converters and lose the natural per-instance change notification. The store stays the *coordination* truth that drives broadcasts; the per-object flags stay the *render* truth kept in sync by those broadcasts.

### Background polling is the only way to catch external stars

OpenSubsonic has no push channel. A star made in Navidrome's web player or on another device is invisible until we ask. So the store is seeded and refreshed via `getStarred2`, whose `starred2.song[]` gives the authoritative starred-song set. `RefreshAsync` diffs the fresh server set against `_starred`, **skipping ids the [write gate](#an-epoch-versioned-write-gate-owns-all-async-results-and-store-writes) rejects** (leased, or written by something newer), applies the surviving differences to `_starred` **first**, then broadcasts them as up to two batched deltas. Updating the store before sending means a synchronous recipient that calls `IsStarred(id)` already sees the new value — the same store-first ordering `ToggleAsync` and `Reconcile` use.

Cadence: **seed once at startup** (after a connected shell is shown), **re-poll on window activation** — via a **new, dedicated `Activated` subscription owned by `MainWindow`** (the single long-lived window), *separate from* the existing titlebar `Activated` handler in transient `ShellPage`, so a recreated shell can't leak stale subscriptions; the handler is gated on `WindowActivationState != Deactivated`, and it **resolves `IFavoritesService` lazily when the event fires** (`App.GetService<…>()` inside the handler) rather than caching it in the `MainWindow` constructor — `App.MainWindow` is constructed before the DI host is built, so a constructor-time resolve would be premature — and a **modest repeating interval (~5 min)** while running. The interval is a single tunable constant.

Cost, accepted: `getStarred2` returns full song metadata for the entire starred set when we only consume ids. It is the only server-side option, and starred sets are typically modest; for very large libraries this is a non-trivial payload per poll.

### Page loads reconcile through the same path (fresh observation wins)

When a page maps songs from the server, each `SongInfo` carries the server's `starred` at load time — usually a *fresh* read. Each load site captures a token with `BeginObservation()` *before* its fetch and, after mapping, calls `Reconcile((id, starred)…, token)`: for any id the [write gate](#an-epoch-versioned-write-gate-owns-all-async-results-and-store-writes) doesn't reject, the store adopts the observation (store-first) and the difference is broadcast, so **other** already-open surfaces are corrected ahead of the next poll.

But the loading page's *own* freshly-built instances can themselves be stale: if a local toggle landed after the page captured its token, `Reconcile` correctly skips that id to protect the toggle — yet the new `SongInfo` was built from the pre-toggle server value and never saw the earlier broadcast (it didn't exist yet). So each load site then **overlays the store onto its instances** — `song.IsStarred = _favorites.IsStarred(song.Id)`. For unprotected ids this is a no-op (the store already adopted the observation); for protected ids it replaces the stale server value with the locally-correct one. After `Reconcile`, the store is the authoritative local truth for every observed id, so overlaying it is always safe — and it's the same "when in doubt, consult the store" rule as the search-play hydration below.

Reconcile sites: `AlbumDetailViewModel`, `PlaylistDetailViewModel`, `ArtistDetailViewModel` (top songs), `TracksViewModel` — after each builds its song list.

### Concurrent toggles for one song are serialized and coalesced

Two surfaces can toggle the same now-playing song at nearly the same instant (player bar, queue row, Now-Playing page all show it). A plain "in-flight set" doesn't define this: overlapping `Star`/`Unstar` calls for one id could complete out of order and leave local and server state disagreeing.

The store keeps, per id, the **latest intended value** (`_desired[id]`) and runs **at most one worker per id**. `ToggleAsync(id, desired)` applies the new intent optimistically (store-first + broadcast) and then: if no worker is active for that id, it *becomes* the worker; if one already is, it just records the new intent and returns.

The worker tracks `serverKnown` — the value it believes is committed on the server, initialised to the store value *before* the toggle that spawned it (no op is in flight for that id then, so store == server). Each pass: if `_desired[id] == serverKnown`, nothing needs sending, so it **settles without a call**; otherwise it issues the matching `Star`/`Unstar`. On success it sets `serverKnown = target` and loops; on failure it leaves `serverKnown` unchanged and — only if `_desired[id]` still equals the failed `target` — reverts the store to `serverKnown` and broadcasts; if a newer intent arrived during the call, it loops and re-evaluates against `serverKnown` instead.

That `serverKnown` check is what stops a redundant call from corrupting state. Example: server false; user stars (intent true, `StarAsync` in flight); user unstars before it returns (intent false); `StarAsync` fails. The latest intent (false) already equals `serverKnown` (false), so the worker settles false — it does **not** fire a needless `UnstarAsync` that, if it too failed, would wrongly flip the heart back to starred. So there is never more than one outstanding `Star`/`Unstar` per id, completions can't interleave, and flip-flopping converges to the user's final intent without spurious calls.

Presence in `_desired` is the per-id **toggle lease**, which doubles as the "pending" signal the [write gate](#an-epoch-versioned-write-gate-owns-all-async-results-and-store-writes) uses to skip in-flight ids in the poll and reconcile diffs.

### An epoch-versioned write gate owns all async results and store writes

The store is mutated by several concurrent, async, externally-timed inputs — user toggles, background `getStarred2` refreshes, page-load reconciles — across connection changes. Rather than bolt a separate guard onto each race, the service runs them all through **one gate**: every async operation and every store write carries an `OperationStamp(epoch, seq)`, and an effect is applied only if the operation **(a) still belongs to the current epoch, (b) still owns its coordination lease, and (c) is not older than the last accepted write for that id.** The fields `_epoch`, `_seq`/`_lastWriteSeq`, `_desired`, and `_refresh` are three facets of this one gate, not separate systems.

**Epoch — connection lifetime.** `_epoch` (a long) is advanced by `Start()` on every connect/reconnect. Advancing it logically abandons all old async work: an old toggle worker or refresh may still be awaiting I/O, but on resumption it finds `stamp.epoch != _epoch` and **discards entirely — no store write, no broadcast, no lease release, no trailing pass.** `Start()` also resets every piece of connection-scoped state — `_starred`, `_desired`, `_lastWriteSeq`, and `_lastFullRefreshSeq` (the absence floor) — and seeds the new epoch immediately, so a switched account never inherits the old server's stars and a slow old refresh never blocks the new one (refresh lease below). (`_seq` stays globally monotonic across epochs — not reset — so observation ordering never collides; the epoch guard, not `_seq`, is what isolates connections.) `BeginObservation()` stamps the epoch into a page's token, so a `Reconcile` from a replaced connection is dropped wholesale.

**Seq — write order within and across reads.** `_seq` is monotonic; every server *read* bumps and captures it at fetch start (`RefreshAsync` internally, a page via `BeginObservation()`). An id's authoritative version is `effectiveSeq(id) = max(_lastWriteSeq[id], _lastFullRefreshSeq)`, and a server observation is accepted for an id only if it is unleased and `obsSeq ≥ effectiveSeq(id)`. Two pieces make this correct:

- **Stamp on *accept*, not on *diff*.** Every accepted observation (and every local mutation) stamps `_lastWriteSeq[id] = its seq` — *even when the value didn't change*. The earlier "stamp only differing survivors" version had a hole: a newer read that *confirms* the store's current value left `_lastWriteSeq[id]` behind, so a later-arriving *older* read that disagreed could still overwrite it (store false; page read at seq 1 saw true; refresh at seq 2 saw false but emitted no diff, so didn't stamp; the seq-1 reconcile then wrote true). Stamping confirmations closes it.
- **A full-refresh floor for absence.** `getStarred2` is a *complete* snapshot of starred ids, but it can't per-id stamp an id that is simply *absent* (not starred, so not in the response, so unknown to the diff). So a successful full refresh raises a global floor `_lastFullRefreshSeq = its obsSeq`, which versions the fact "every id not otherwise more-recently written is known as of this seq." Folding it into `effectiveSeq` lets the refresh reject an older page observation that claims an id is starred which the newer full snapshot showed absent — without materialising a dictionary entry per non-starred id. (Per-id `_lastWriteSeq` entries at or below the floor are redundant and may be pruned after each full refresh.)

So a committed toggle (stamped on confirmation) can't be regressed by an earlier-started refresh, an *older* server read can't overwrite a *newer* one whether or not either changed a value, and a genuinely newer external change (higher seq) still applies.

**Lease — temporary ownership of mutable coordination.** Two pieces of coordination are owned, not shared:

- A **per-id toggle lease** = presence in `_desired`. The first `ToggleAsync` for an unleased id becomes its worker and runs the [coalescing loop](#concurrent-toggles-for-one-song-are-serialized-and-coalesced); a later one just updates the leased intent. A worker releases the lease (removes `_desired[id]`) **only on a clean settle/revert under its own epoch — never from a `finally`** — because a `Start()` reset clears `_desired` and a new-epoch worker may already have re-leased the id; a stale worker must not delete the new owner's intent.
- A **refresh lease** = `_refresh`, a small **sealed reference type** `RefreshLease { long Epoch; Task Task; bool Queued; }` owned by the epoch that started it — the `Queued` "run once more" flag lives *inside* the lease, so it is epoch-scoped like everything else. It must be a reference type with a *mutable* `Queued`: ownership is checked by **`ReferenceEquals(_refresh, myLease)`**, not value equality, so a stale copy can neither be mistaken for the live lease nor lose a `Queued` write (a `record struct` would break both). `RefreshAsync` runs a fetch only if no lease is held *for the current epoch*; otherwise it sets the current lease's `Queued` and returns its task (callers are fire-and-forget, so completing before the trailing pass is immaterial — the trailing pass guarantees eventual freshness). When a refresh finishes it clears the lease and starts the single trailing pass **only if `_refresh` is still (by reference) its own lease** (epoch current); an old-epoch refresh whose lease was superseded by a new epoch's seed does neither, and its stale `Queued` is discarded with it — so a new connection seeds immediately even while an old refresh is in flight, the old one's `finally` is inert, and the new epoch can't inherit an old `Queued`. Single-flighting also collapses overlapping startup/activation/timer refreshes into at most two full `getStarred2` payloads per burst.

### Newly materialized tracks hydrate from the store

Delta-only messages keep *existing* instances in sync but say nothing about an instance created *after* the fact. The search-play path (`ShellViewModel.HandleSuggestionChosen`) builds a `PlaybackTrack` from a `SearchSuggestionItem` that carries no `starred` field; if the store was already seeded and the server already agrees, no future poll delta will ever fire for it, so it would stay falsely unstarred indefinitely.

Rule: **any materialization of a `SongInfo`/`PlaybackTrack` that lacks a reliable server `starred` value seeds `IsStarred` from `_favorites.IsStarred(id)`.** This is the concrete consumer of the otherwise-rarely-needed `IsStarred` query. The four `BuildPlaybackTracks` sites copy from `SongInfo.IsStarred` (reliable, from the server response) and are unaffected; the search-play site is the one that must consult the store.

### `PlaybackTrack` gets a hand-rolled `INotifyPropertyChanged` (Core stays dependency-free)

The queue flyout binds a `QueueItem` (observable) wrapping a plain `PlaybackTrack`. For a queue-row heart to live-update, its bound boolean must notify. `PlaybackTrack` lives in `Hypersonic.Core`, which is deliberately dependency-free (no CommunityToolkit.Mvvm). We give `PlaybackTrack` a minimal hand-written `INotifyPropertyChanged` where **only `IsStarred`** raises `PropertyChanged`; every other property stays a plain auto-prop. The queue heart binds `Track.IsStarred` directly — one notifying field, the per-track source of truth, no second mirror to keep in sync.

The notification only helps if the binding listens: **`x:Bind` defaults to `OneTime`**, and the existing queue-row bindings (`Track.Title`, etc.) are static one-time. So the heart's glyph / foreground / checked bindings to `Track.IsStarred` must be **`Mode=OneWay`** explicitly (`{x:Bind Track.IsStarred, Mode=OneWay, …}`) — or use a classic `{Binding Track.IsStarred}`, which is `OneWay` by default — otherwise the row won't redraw when the broadcast flips the flag.

Rejected: an observable `IsStarred` mirror on `QueueItem` (two booleans per queued track to keep equal — multiplies the existing player-mirror smell per row); and adding CommunityToolkit.Mvvm to Core (introduces the first dependency into a library kept clean on purpose).

### Subscriptions are always-active and weakly held

Participating VMs pass the injected `IMessenger` to `base(messenger)`, set `IsActive = true` in their constructor, and implement `IRecipient<FavoritesChangedMessage>`. `WeakReferenceMessenger` holds weak references, so a navigated-away transient VM is collected with no leak and no explicit unregister. "Always active" (rather than registering in `OnNavigatedTo` and unregistering in `OnNavigatedFrom`) means an in-memory list that is off-screen still stays current, so returning to it never shows a stale heart — and it removes the need for page-load reconcile to cover the "deactivated and missed a broadcast" case.

### UI-thread affinity, with an explicit broadcast-dispatch rule

Every entry point runs on the UI thread: toggles fire from commands, reconcile from VM load handlers, refresh from the activation handler / `DispatcherQueueTimer` tick / startup seed (whose `await` continuations resume on the captured UI `SynchronizationContext`). So the store's state (`_starred`, `_desired`, the version maps) is single-threaded and needs no lock.

Broadcast dispatch has one explicit rule, because two requirements pull in opposite directions: the clicked row must update *reentrantly* (so the optimistic flip is instant), yet a stray off-thread caller must not raise observable properties on a background thread. So the service captures the UI `DispatcherQueue` at start and, when broadcasting, **sends inline when it already has UI-thread access, and only enqueues onto the dispatcher when called from off-thread**:

```
if (_dispatcher.HasThreadAccess) Send(msg);        // inline → reentrant, instant clicked-row update
else _dispatcher.TryEnqueue(() => Send(msg));      // defensive off-thread path only
```

In practice almost every broadcast is inline (all entry points are UI-thread); the enqueue branch is a safety net that never fires on the happy path but guarantees correctness if some future caller arrives off-thread.

### Graceful degradation

If `getStarred2` is unsupported or fails, the service logs and leaves the store untouched — toggles and same-session cross-surface sync still work; only external-change detection is lost. If `Client` is null (disconnected), `ToggleAsync` no-ops *before* any optimistic broadcast, so the heart doesn't flicker, and `RefreshAsync` no-ops.

---

## Architecture

```
Hypersonic.Core/Models/
└── PlaybackTrack.cs                 + minimal INotifyPropertyChanged (only IsStarred notifies)

Hypersonic/Messages/                 (new folder)
└── FavoritesChangedMessage.cs       record (bool IsStarred, IReadOnlyList<string> Ids)

Hypersonic/Contracts/Services/
└── IFavoritesService.cs             (new)

Hypersonic/Services/
└── FavoritesService.cs              (new) — single writer, store, poll, reconcile, broadcast

Hypersonic/ViewModels/
├── PlayerViewModel.cs               delegate toggle to service; IRecipient handler; delete RevertStar
├── TracksViewModel.cs              ┐
├── AlbumDetailViewModel.cs        ├ delegate toggle; IRecipient handler; BeginObservation + Reconcile + overlay on load;
├── PlaylistDetailViewModel.cs     │  ArtistDetail also deletes SyncStarInBothCaches
├── ArtistDetailViewModel.cs       ┘
├── ShellViewModel.cs                hydrate search-play PlaybackTrack via IsStarred(id)
└── ConnectViewModel.cs              call Start() after manual connect

Hypersonic/Views/
├── ShellPage.xaml                   queue-row heart (bind Track.IsStarred)
└── NowPlayingPage.xaml              now-playing heart (bind Player.IsStarred) + queue-row heart

Hypersonic/
├── App.xaml.cs                      register IFavoritesService singleton
├── Services/ActivationService.cs    call Start() on the auto-connect path (shell shown with a client)
└── MainWindow.xaml.cs               new dedicated Activated subscription → RefreshAsync (separate from ShellPage's titlebar handler)
```

DI:

```csharp
services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
services.AddSingleton<IFavoritesService, FavoritesService>();
```

`FavoritesService` depends on `ISubsonicClientService` (for `Client`), `ILoggingService`, and `IMessenger` — all singletons. The service **takes `IMessenger` by injection** rather than touching `WeakReferenceMessenger.Default` statically, and participating VMs pass the same instance to `base(messenger)` on `ObservableRecipient`. In production that instance *is* `WeakReferenceMessenger.Default`; in tests each case constructs the service and VMs with a **fresh `WeakReferenceMessenger`**, so weak registrations from earlier tests' still-alive VMs can't make broadcast counts flaky. (Equivalent fallback if a test does use `.Default`: reset/`UnregisterAll` in cleanup.)

---

## Service contract

```csharp
public readonly record struct OperationStamp(long Epoch, long Seq);

public interface IFavoritesService
{
    bool IsStarred(string songId);                          // store lookup — hydrate/overlay instances
    OperationStamp BeginObservation();                      // capture (epoch + seq) before a fetch
    Task ToggleAsync(string songId, bool desired);          // coalesced per-id toggle (optimistic + API + revert)
    Task RefreshAsync();                                    // getStarred2 → gated diff → store-first → broadcast
    void Reconcile(IEnumerable<(string Id, bool Starred)> observed, OperationStamp since);  // gated reconcile
    void Start();                                           // advance epoch, capture dispatcher, start timer, reset + reseed
}
```

State (all UI-thread; no lock). These fields implement the one [write gate](#an-epoch-versioned-write-gate-owns-all-async-results-and-store-writes):

- `_starred : HashSet<string>` — current known starred ids; the render-coordination truth.
- `_epoch : long` — connection lifetime; advanced by every `Start()`. Captured by every async op; a result whose `Epoch != _epoch` is discarded entirely.
- `_seq : long`, `_lastWriteSeq : Dictionary<string, long>`, and `_lastFullRefreshSeq : long` — write order. `_seq` bumps on every local mutation and every server-read start and stays **globally monotonic** (never reset). An id's authoritative version is `effectiveSeq(id) = max(_lastWriteSeq[id], _lastFullRefreshSeq)`; `_lastWriteSeq[id]` is stamped on every **accepted** write to that id (local mutation, or a server observation — *including a no-diff confirmation*), and `_lastFullRefreshSeq` is the floor a successful full `getStarred2` sets to version the "absence ⇒ not starred" knowledge for ids it doesn't individually carry. `_lastWriteSeq` and `_lastFullRefreshSeq` are **connection-scoped** (reset by `Start()`); `_seq` is not.
- `_desired : Dictionary<string, bool>` — the per-id **toggle lease**: ids with an active worker → their latest intended value. **Presence ⇒ leased/pending.**
- `_refresh : RefreshLease?` — the **refresh lease**: a sealed reference type `{ long Epoch; Task Task; bool Queued; }` owned by the epoch that started it, with ownership checked by `ReferenceEquals` (not value equality) and a mutable `Queued`. Because the flag lives *inside* the lease, it is epoch-scoped by construction: a fresh lease for a new epoch starts `Queued = false`, so a request queued under an old epoch can never make a new epoch run a trailing pass.

Each toggle worker also holds a local `serverKnown` (believed-committed value) and the `_epoch` it started under.

Behavior:

- **`ToggleAsync(id, desired)`** — no-op if `Client` is null (before any optimistic change). Apply optimistically, store-first: bump `_seq`, stamp `_lastWriteSeq[id]`, set `_starred`, set `_desired[id] = desired`, broadcast `(desired, [id])`. If a worker already leases `id`, return. Otherwise become the worker (capturing `epoch = _epoch` and `serverKnown` = the pre-toggle store value) and loop: if `_desired[id] == serverKnown`, settle without a call; else issue `Star/UnstarAsync`. After each await, **if `_epoch != epoch` the worker returns immediately and does *not* touch `_desired`** — the `Start()` reset already cleared it and a new-epoch worker may have re-leased the id, so a `finally`-style cleanup here would delete the new epoch's intent. On success set `serverKnown = target`, bump `_seq` + stamp `_lastWriteSeq[id]`, loop. On failure with `_desired[id]` still `== target`, revert `_starred` to `serverKnown`, bump `_seq` + stamp, broadcast `(serverKnown, [id])`, log a warning, settle; if a newer intent arrived, loop. The lease (`_desired[id]`) is released **only** on a clean settle/revert under the worker's own epoch — never from a `finally`. The initiating call awaits to settlement; concurrent same-id calls return after their optimistic apply.
- **`RefreshAsync`** — the refresh lease is epoch-owned: if `_refresh` is held **for the current epoch**, set its `Queued = true` and return its `Task`; otherwise install a fresh `RefreshLease` for `_epoch` (with `Queued = false`) and run the body. In the body's `finally`, **only if `ReferenceEquals(_refresh, thisLease)`** (same epoch, not superseded by a `Start()`), clear it and — if its `Queued` was set — start exactly one trailing pass. The returned task completes at the end of the *current* pass and may finish before the trailing pass; that's fine because every caller (startup seed, activation, timer) is fire-and-forget and the trailing pass guarantees eventual freshness. Body: no-op if `Client` is null; capture `epoch = _epoch` and `obsSeq = ++_seq`; `getStarred2`; if `_epoch != epoch` discard; on `status=="failed"` log and return. Build the server starred-song-id set `S`; for each id in `S ∪ _starred`, **skip if leased or `effectiveSeq(id) > obsSeq`**; for survivors, set `_starred` membership to `id ∈ S`, **stamp `_lastWriteSeq[id] = obsSeq` whether or not the value changed**, and accumulate a delta only when it changed. Apply store + stamps first, then `_lastFullRefreshSeq = max(_lastFullRefreshSeq, obsSeq)` (the floor that versions every *absent* id), then broadcast the two batched deltas. Optionally prune `_lastWriteSeq` entries `≤ _lastFullRefreshSeq`. Exceptions logged, store untouched.
- **`Reconcile(observed, since)`** — if `since.Epoch != _epoch`, ignore the whole observation (stale connection). Otherwise for each `(id, starred)`, **skip if leased or `effectiveSeq(id) > since.Seq`**; for survivors, **stamp `_lastWriteSeq[id] = since.Seq` even on a no-diff confirmation**, and when the value differs update `_starred` (store-first) and accumulate a delta; then broadcast. A page is a *partial* read, so it never raises `_lastFullRefreshSeq`. Synchronous on the UI thread. (The caller then overlays `IsStarred(id)` onto its own loaded instances — see [page-load reconcile](#page-loads-reconcile-through-the-same-path-fresh-observation-wins).)
- **`BeginObservation`** — returns `new OperationStamp(_epoch, ++_seq)`; a page captures this before its load fetch and passes it to `Reconcile`, so its observation is stamped at fetch-start.
- **`Start`** — idempotent for setup: on first call captures `DispatcherQueue.GetForCurrentThread()` and starts the repeating interval timer. On every call **advances `_epoch`** and resets all connection-scoped state — `_starred`, `_desired`, `_lastWriteSeq`, and `_lastFullRefreshSeq` (back to 0) — then fires a `RefreshAsync` seed, which under the new epoch installs a fresh `RefreshLease` (`Queued = false`) and runs immediately regardless of any old-epoch refresh still in flight, so no stale `Queued` or absence floor survives the epoch change. Called from both the auto-connect path (`ActivationService`) and the manual-connect path (`ConnectViewModel`), so neither misses the seed and a switched connection can't leak the old server's stars.

The pure diff (old set + server set, with the lease/version skips → added/removed) is factored as an internal static helper for direct unit testing, with the service tested end-to-end against the fakes.

---

## Message and subscriber handlers

```csharp
public sealed record FavoritesChangedMessage(bool IsStarred, IReadOnlyList<string> Ids);
```

Each subscriber updates only the instances it owns, matching by an id set built from `message.Ids` (so duplicate ids — repeated rows, the artist community/personal lists — all update):

- **Track-list VMs** set `IsStarred` on every owned `SongInfo` whose id is in the set.
- **`PlayerViewModel`** sets `IsStarred` on every `PlaybackTrack` in the effective queue whose id is in the set (which updates the bound queue rows, since the queue binds those instances), and — only if `NowPlaying.Id` is in the set — updates its own mirror `IsStarred`.

Toggling delegates: `TracksViewModel.ToggleStarAsync(song)` becomes `_favorites.ToggleAsync(song.Id, !song.IsStarred)`; the queue-row command becomes `_favorites.ToggleAsync(item.Track.Id, !item.Track.IsStarred)`; the player-bar `ToggleStarCommand` becomes `_favorites.ToggleAsync(NowPlaying.Id, !IsStarred)`.

---

## UI surfaces

- **Now-Playing page heart** — a heart bound to `Player.IsStarred` / `Player.ToggleStarCommand`, reusing the player-bar VM state so the two are automatically in lockstep. Same glyphs (`&#xEB52;` filled / `&#xEB51;` outline) and `BoolToFavoriteBrushConverter` as the player bar.
- **Queue-row hearts** (in both `ShellPage.xaml` and `NowPlayingPage.xaml` queue templates) — a per-row heart bound to `Track.IsStarred` **with `Mode=OneWay`** (x:Bind is one-time by default; see [PlaybackTrack INPC](#playbacktrack-gets-a-hand-rolled-inotifypropertychanged-core-stays-dependency-free)), command bound to a `ToggleQueueItemStar(QueueItem)` relay on `PlayerViewModel`.

---

## Edge cases

| Scenario | Behavior |
|---|---|
| Track changes during an in-flight toggle that then fails | Revert message is id-keyed; player mirror untouched (id ≠ new `NowPlaying.Id`); old track instance reverts |
| Two surfaces toggle the same song at once | One worker per id; calls serialized; latest intent wins; no out-of-order completion |
| Star then quickly unstar (back to server state) while the first call fails | Worker's `serverKnown` shows the latest intent already matches the server, so it settles without a redundant opposite call |
| Poll/reconcile snapshot was taken before a local toggle committed | Write gate skips the id (`effectiveSeq(id) > obsSeq`, stamped on confirmation); next clean read reconciles |
| Older page read lands after a newer refresh (server-vs-server) | Newer read's higher `effectiveSeq(id)` (per-id stamp or full-refresh floor) rejects the older observation; a later read with a higher `obsSeq` still applies genuine external changes |
| Startup + activation + timer refresh overlap | Epoch-owned refresh lease runs one `getStarred2` at a time and coalesces the rest into one trailing pass — no duplicate payloads, ordered writes |
| Page instance built from a stale read whose id the gate rejected | Load site overlays `IsStarred(id)` from the store onto its instances, so the row shows the locally-correct heart |
| Server/account switched while an old request is in flight | `_epoch` mismatch discards the old response/worker/observation (no write, broadcast, lease release, or trailing pass); new epoch seeds immediately |
| `getStarred2` unsupported / fails | Logged; store untouched; toggles + in-session sync still work |
| Disconnected (`Client == null`) | `ToggleAsync` no-ops before any optimistic broadcast (no flicker); poll no-ops |
| Same song on multiple surfaces / duplicate rows | Handlers update **all** matching instances via id-set membership |
| External unstar, then user opens a page containing it | `Reconcile` corrects the store + broadcasts before the next poll |
| Song started from a search suggestion (no star field) | `PlaybackTrack.IsStarred` hydrated from `IsStarred(id)` at creation, so it's correct even with no future delta |
| First connection is manual (no saved creds) | `ConnectViewModel` calls the idempotent `Start()`, so the store seeds on the manual path too |

---

## Test strategy

Reuse the existing fakes (`RecordingSubsonicClient` with `Gate` / `ResponseStatus` / `StarShouldThrow`, `FakeSubsonicClientService`, `NullLoggingService`). **Extend** `RecordingSubsonicClient` with a configurable `GetStarred2Async` (a settable starred-song-id set; optional throw / `status=="failed"`), replacing its current `NotImplementedException`.

**Messenger isolation:** every test constructs the service and any VMs with a **fresh `WeakReferenceMessenger`** instance (passed as `IMessenger`), never the shared `.Default` — so weak registrations from a prior test's still-alive VMs can't leak into another test's broadcast counts. (If a test must use `.Default`, it resets/`UnregisterAll` in cleanup.)

`FavoritesServiceTests` (`Hypersonic.Tests`):

- Toggle success — `StarAsync` called with the id, store updated, one `(true, [id])` broadcast.
- Toggle unstar — `UnstarAsync` called, store cleared, `(false, [id])`.
- Toggle throws — store reverts, revert broadcast emitted.
- Toggle `status=="failed"` (no exception) — store reverts, revert broadcast emitted.
- Toggle while `Client == null` — no broadcast, no store change.
- **Concurrent same-id toggles** (`Gate`) — two overlapping toggles for one id issue only one outstanding `Star`/`Unstar` at a time, converge to the latest intent, and never complete out of order.
- **Superseded failed call settles to confirmed state** — server false; star (gated, in flight); unstar; the gated `StarAsync` then fails; assert the worker settles `false` and issues **no** `UnstarAsync` (final store false, no spurious revert to true).
- **Stale-snapshot guard (local)** — a `RefreshAsync` whose `getStarred2` is gated to return *after* a toggle settles does **not** regress that id (the `effectiveSeq(id) > obsSeq` skip, stamped on confirmation); a later refresh with a higher `obsSeq` does apply a genuine external change for the same id.
- **Server-vs-server ordering** — an older observation (lower `obsSeq` — e.g. a page token captured before a refresh) does **not** overwrite the value a newer refresh already wrote; a later observation with a higher `obsSeq` does.
- **No-diff confirmation still orders** — store `false`; page captured at seq 1 sees `true`; a refresh at seq 2 sees `false` (no diff vs store) yet stamps/floors id at 2; the seq-1 reconcile is then rejected (no false→true regression). Also covers the symmetric absence case via `_lastFullRefreshSeq`: a full refresh rejects an older page that claims an id starred which the snapshot showed absent.
- **Refresh single-flight** — overlapping `RefreshAsync` calls (gated) issue only one `getStarred2`, and a request arriving mid-flight produces exactly one trailing pass (not N); the in-flight task is returned to the extra callers.
- **Epoch guard discards replaced-connection work** — a `RefreshAsync` (and a toggle worker) whose response is gated until after a `Start()` (new epoch) is discarded: the new store is not populated/reverted from the old connection, no broadcast fires, and the old worker does not release the new epoch's lease; a `Reconcile` with a stale-epoch token is ignored.
- **New epoch seeds immediately; old refresh's `finally` is inert** — with an old-epoch `RefreshAsync` still gated (and its lease's `Queued` set by a second old-epoch call), a `Start()` (new epoch) installs a fresh lease and seeds right away; when the old refresh later completes it neither writes, clears `_refresh`, nor starts a trailing pass — the stale `Queued` dies with the old lease and the new epoch runs no extra pass.
- `RefreshAsync` diff — server adds/removes vs store produce the correct two batched deltas; leased and newer-version ids skipped; store updated before broadcast.
- `RefreshAsync` failure / `status=="failed"` — no crash, store untouched.
- **`Reconcile` token gate** — observation differing from store updates store + broadcasts; equal observation is silent; an id with a newer `_lastWriteSeq` than the token's `Seq` is skipped; a stale-epoch token is fully ignored.
- **`Start` idempotency + reset** — first call starts the timer once and seeds; a second call advances the epoch and clears **all** connection-scoped state — `_starred`, `_desired`, `_lastWriteSeq`, and `_lastFullRefreshSeq` (asserted reset to 0) — then re-seeds without duplicating the timer.
- `IsStarred(id)` reflects the store after seed/toggle — backs the search-play hydration and the load-site overlay.
- Pure diff helper — added/removed computation including the pending/version skips.

Cross-VM sync (`Hypersonic.Tests`):

- A `FavoritesChangedMessage` updates all matching `SongInfo` across two registered VM instances, the queue's `PlaybackTrack` instances, and the player mirror; non-matching ids are left alone.

Load-site reconcile + overlay (`Hypersonic.Tests`, one representative list VM):

- A VM that captures its token, then has a toggle land, then loads a page whose mapped `SongInfo` carries the pre-toggle value: after `Reconcile` + overlay, the loaded instance shows the store's (post-toggle) value, and the store itself is not regressed.

`PlayerViewModel` (`Hypersonic.Tests`):

- Toggle delegates to the service (assert via `RecordingSubsonicClient` call counts).
- Message containing `NowPlaying.Id` updates the mirror; message for a different id leaves the mirror unchanged (the race case).
- Existing player-bar star tests are rewritten from the bespoke optimistic/revert model to the service-delegation + broadcast model.

`PlaybackTrack`:

- `IsStarred` raises `PropertyChanged`; other property writes do not.

`ShellViewModel` (`Hypersonic.Tests`):

- Search-play builds a `PlaybackTrack` whose `IsStarred` is hydrated from `IsStarred(id)` (seeded store → starred track plays as favorited even though the suggestion carried no star field).

---

## Deferred / out of scope

- **Album / artist stars.** The store and message are id-based and `Star/UnstarAsync` already accept `albumId`/`artistId`, so this is additive — but no album/artist star UI exists, so we don't build the sync for entities with no entry point.
- **Search-result star state.** `SearchSuggestionItem` carries no star state today; wiring a heart into the search popup is a separate change.
- **Retiring the `PlayerViewModel.IsStarred` mirror.** Now that `PlaybackTrack.IsStarred` notifies, the player bar could bind through the track instead of a mirror. Possible later cleanup; not done here to keep the change focused.
- **Server push / real-time external sync.** OpenSubsonic offers none; polling is the ceiling. Tightening latency (e.g., poll-on-navigation) is a tuning follow-up.

---

## References

- Player-bar favorite button: `docs/design/ui/2026-06-07-player-bar-favorite-button.md` (and the matching implementation doc).
- Existing toggles: `Hypersonic/ViewModels/{Tracks,AlbumDetail,PlaylistDetail,ArtistDetail,Player}ViewModel.cs`.
- Status-check precedent: `Hypersonic/ViewModels/PlayerViewModel.cs` (star), `NowPlayingViewModel.cs` (lyrics, ~233/267), `ConnectViewModel.cs`.
- API: `ISubsonicClient.Star/UnstarAsync`, `GetStarred2Async` (`starred2.song[]`); client via `ISubsonicClientService.Client`.
- Models: `Hypersonic.Core/Models/PlaybackTrack.cs`, `Hypersonic/Models/SongInfo.cs`, `Hypersonic/Models/QueueItem.cs`.
- Fakes: `Hypersonic.Tests/Fakes/{RecordingSubsonicClient,FakeSubsonicClientService,NullLoggingService}.cs`.
