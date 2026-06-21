# Shared Playback-Progress Clock + Read-Only Playback State — Design

Make track playback progress a single, smoothly-interpolated fact that any surface can observe, and give the playback engine a read-only face so observers physically cannot mutate it.

Two coupled changes built on one idea: **the authoritative position is a coarse anchor sampled every ~500 ms; each surface interpolates it on its own timebase.** The lyrics renderer already does this internally; we generalize the primitive and apply it to the player bar, and along the way split the playback interface along the observe-vs-command seam.

---

## What we're building

`IPlaybackService.Position` is a `double` (seconds) refreshed by a 500 ms `DispatcherQueueTimer` (`PlaybackService.OnPositionTimerTick`). The `double` loses no precision; the **publish cadence** is the limiter. Forwarding that raw 2 Hz value to a UI makes motion step.

The lyrics canvas already solved this for itself: `LyricsCanvasControl` owns a `PlaybackClock` that treats the incoming position as a *clock anchor, not per-frame truth*, free-runs against the Win2D frame clock between anchors, and folds each authoritative update back in as a snap (large delta) or a smooth converge (small delta). The wipe is fluid because it estimates position **at its own render-frame timestamp**.

We make two moves:

1. **Promote `PlaybackClock` to shared infrastructure.** Relocate the (already pure, dependency-free) estimator into `Hypersonic.Core` so the player bar — and any future progress consumer — reuses the same tested primitive. Each consumer owns its **own instance**, fed by its **own "now" source** on its **own thread**. There is no shared instance and no central broadcast of a smoothed value (see [Why not one shared clock](#why-the-shared-thing-is-the-type-not-an-instance)).

2. **Introduce `IReadOnlyPlaybackState`.** A read-only role-interface that `IPlaybackService` extends, implemented by the same `PlaybackService` singleton. Pure observers inject the narrow interface and are compiler-barred from mutating playback; components that issue commands keep `IPlaybackService`.

The visible payoff is a smooth, frame-interpolated player-bar thumb. The structural payoff is a reusable interpolation primitive and an observe-vs-command split that narrows the concurrency-sensitive write surface. **Lyrics behavior is unchanged.**

Scope is deliberately the **progress-sync slice**: the read interface starts with only the members the current observers need, and the only new interpolating consumer is the player bar. Growing the read-model and migrating other readers is additive, later work.

---

## Pivotal decisions

### Same-object role interface — zero runtime cost

`IPlaybackService : IReadOnlyPlaybackState`. The read members (`NowPlaying`, `Position`, `Duration`, `IsPlaying`, and the three observation events) **move up** into the base — they are not duplicated. `PlaybackService` already implements every one of them, so there is no implementation change.

DI stops binding the interface straight to the implementation and instead registers the concrete singleton once, then maps both interfaces onto that single instance:

```csharp
services.AddSingleton<PlaybackService>();
services.AddSingleton<IPlaybackService>(sp => sp.GetRequiredService<PlaybackService>());
services.AddSingleton<IReadOnlyPlaybackState>(sp => sp.GetRequiredService<PlaybackService>());
```

Because both handles resolve to the **same object**, observing through `IReadOnlyPlaybackState` reads the same fields and subscribes to the same events as `IPlaybackService` — no extra indirection hop, no propagation delay, no staleness window. The narrow interface is a compile-time view that removes only the *ability* to call mutators; at runtime it is indistinguishable from reading the service directly. This is what makes the split free: it buys responsibility-clarity and a narrowed write surface at no observable cost.

The alternative — a separate `PlaybackProgress` object that subscribes to the service and re-publishes — is explicitly **rejected**: it would insert a synchronous re-raise hop and a theoretical staleness window for no benefit, since the data already lives on the service.

### Why the shared thing is the *type*, not an instance

`PlaybackClock` is **not thread-safe** (mutable fields, no locks), and the two consumers do not share a thread or a clock source:

- **Lyrics** ticks its clock on the **Win2D render thread**, with "now" supplied by the animated control's frame clock (`() => _frameNowMs`, set from `args.Timing.TotalTime` each draw).
- **Player bar** will tick on the **UI thread**, with "now" supplied by a monotonic `Stopwatch`.

The smoothing is inherently a per-consumer, per-frame computation: a surface is fluid precisely because it estimates the position **at its own frame's timestamp**. A central service that ticks one clock and broadcasts a single `SmoothedPosition` scalar cannot preserve that — it can be no smoother than its publish rate, and feeding render frames a value sampled on a different, phase-offset clock reintroduces the judder the local clock exists to remove. So the unit of sharing is the **class** (a reusable, tested estimator), instantiated once per consumer. The authoritative anchor is shared read-only truth; the estimator that turns it into a continuous clock is private to each surface.

### `PlaybackClock` moves to Core, behavior frozen

The estimator depends on nothing but `System.Func<double>` and `System.Math` — the same purity bar as `PlaybackQueue`. It relocates from `Hypersonic/Controls/Lyrics/PlaybackClock.cs` to `Hypersonic.Core` (alongside `PlaybackQueue`), and its tests move from `Hypersonic.Tests` to `Hypersonic.Core.Tests`, where they run on plain `dotnet test` with no `-p:Platform=x64`. The consumer supplies the "now" source, so the `Stopwatch` the player bar uses lives in the *consumer*, not in the clock — the clock stays pure.

The class is `internal` today (usable only within the app assembly). Moving it to Core means the WinUI app can no longer see it unless it is exposed, so it **becomes `public`** in Core — matching `PlaybackQueue`, which is `public` for the same cross-assembly reason. (Granting Core `InternalsVisibleTo("Hypersonic")` is the alternative and is rejected: `PlaybackClock` is now deliberately shared API, not an internal detail.)

**Unit contract — milliseconds.** `PlaybackClock` is millisecond-based and stays that way: its constants are ms (`SeekThresholdMs = 250`, `ConvergenceTimeConstantMs = 120`), the lyrics control already feeds it ms (`_frameNowMs` from `args.Timing.TotalTime.TotalMilliseconds`, and a `PositionMs` anchor that `NowPlayingViewModel` builds as `Position * 1000`). We do **not** re-unit the clock — lyrics depend on the ms timebase. Instead, **every consumer converts at the boundary**: the now-source is ms, the anchor is ms, and the estimate is read back in ms. The player bar therefore mirrors what the lyrics VM already does — multiply seconds in, divide ms out (see below).

The lyrics control changes only its `using` for the new namespace. Its render-thread clock, the `() => _frameNowMs` Win2D feed, and the tuned constants are untouched. Those constants stay **per-instance**, so the player bar can tune its own feel without perturbing the wipe.

### Player-bar smoothing lives in `PlayerViewModel`

`PlayerViewModel` already mediates `Position`: it subscribes to the service's `PositionChanged` (500 ms) and pushes the value into its bound `Position` property under an `_isExternalUpdate` guard, while a user drag flows the other way into `Seek`. We extend exactly this mediator rather than introduce a new control.

The VM gains a private `PlaybackClock` and a ~30 fps `DispatcherQueueTimer` — the same timer primitive it already uses to debounce volume saves. Because the clock is millisecond-based, the VM converts at the boundary: the now-source is `Stopwatch.Elapsed.TotalMilliseconds`, anchors are `serviceSeconds * 1000`, and the estimate is read back as `Estimate() / 1000` before it is written into the seconds-based `Position`. This is the same seconds↔ms boundary `NowPlayingViewModel` already crosses.

The clock is **anchored by the three events the VM already handles**, so its timeline never lags the engine:

- `PositionChanged` → `clock.SetPosition(servicePositionSeconds * 1000)`,
- `PlaybackStateChanged` → `clock.SetPlaying(isPlaying)` (and start/stop the tick),
- `TrackChanged` → `clock.SetPosition(0)`. **This anchor is essential**: `PlaybackService.PlayCurrent` resets `Position` to 0 and fires `TrackChanged` *without* a `PositionChanged`, so on Next/Previous while playing, an un-re-anchored clock would keep estimating the *previous* track's (further-along) timeline until the next 500 ms tick — the thumb would jump forward, then snap back to 0. Re-anchoring on `TrackChanged` makes the new track start cleanly at 0.

Writes into `Position` go through the existing `_isExternalUpdate` guard, so the frame-rate push never triggers a `Seek`. The thumb advances sub-pixel per frame for any normal track length, so 30 fps is comfortable headroom — the rate is not sensitive, and `CompositionTarget.Rendering` is a drop-in alternative if true per-frame motion is ever wanted.

**One write rule, order-independent.** Rather than gate the displayed value on "is the tick running?" — which is fragile because `Stop()` fires `PositionChanged` *before* `PlaybackStateChanged`, so at the position event the local tick has not yet been told to stop — the rule is simply:

- **`PositionChanged` always anchors and (unless scrubbing) writes `Position` once** from the fresh estimate. This renders stops, paused seeks, and Next/Previous-while-paused immediately, regardless of tick state.
- **The tick additionally writes `Position` while playing**, filling the frames between anchors.

The two writers never conflict because both write the same `Estimate()`; the per-event write just guarantees a stop/seek lands even when no tick will follow. The tick still only *runs* while playing (frozen estimate when paused), but correctness no longer depends on its running state.

**Semantic re-anchors snap; only the routine playing anchor converges.** `PlaybackClock` smooths sub-threshold (≤250 ms) corrections by holding the current estimate and decaying toward the new value — right for the *routine* 500 ms playback anchor (it hides the few-ms drift between the wall clock and the engine), but wrong for a *semantic* discontinuity that happens to be small: a stop-to-0 near the start, a Next/Previous when the old estimate was <250 ms in, or a small keyboard/UIA nudge would briefly show the old value or drift back toward it. So the clock exposes an additive `hardSnap` option (default off, so lyrics are untouched), and the player bar hard-snaps every re-anchor it knows is semantic: `TrackChanged`→0, a user-originated write (seek/drag-end), and — on the `PositionChanged` event — any anchor that arrives while **not playing** (routine ticks fire only while playing, so a not-playing position event is always a stop/clear/paused-seek). The routine playing anchor keeps converging.

`PositionText` stays second-granular: it formats to whole seconds, so it is updated only when the integer second changes, not every tick.

### The fiddly bit: scrub gating

The `Slider` binds `Value="{x:Bind Player.Position, Mode=TwoWay}"` and has **no interaction tracking today**. With a per-frame push into `Position`, the estimator would fight the user. Two distinct cases, handled at two levels:

**User-originated value changes (device-agnostic) re-anchor the clock.** Any user change to the slider — keyboard arrows, gamepad, UI Automation, or clicking the track — flows through the bound setter and is distinguishable from the estimator's own writes by the existing `_isExternalUpdate` discriminator (the setter already calls `Seek` only when the change is *not* external). The rule generalizes that: a user-originated write also `SetPosition`s the clock to the new value, so the timeline re-anchors to wherever the user put it and the tick continues smoothly from there. This covers every input modality uniformly, not just the pointer.

**Continuous pointer drag additionally suspends the push.** So the thumb tracks the finger pixel-for-pixel (not the 30 fps estimate) during a drag, a pointer interaction additionally suspends the estimator's writes for its duration, then re-anchors and resumes on release. Two hard requirements on this suspend:

- It must **fail safe** — it can never get stuck suspended. The suspend begins on the press and resumes on *any* end of the interaction. The slider captures the pointer on press, so a real drag always ends with capture loss (which fires on release, cancel, capture-steal, *and* window deactivation); a press that never captures still ends with a release/cancel. Routing all of these end signals to one idempotent resume — plus a `TrackChanged` self-heal in the VM as a final backstop — makes the orphaned-suspend state unreachable in practice, with no timeout and no continuous pointer-state polling. (WinUI exposes no "capture-gained" event, so the begin is the press; keyboard/UIA changes take no capture and never trigger an end mid-interaction, so they correctly skip the suspend and flow through the user-write re-anchor instead.)
- The **exact event wiring is deferred to the implementation doc.** This design fixes the *contract* (user-originated writes re-anchor; pointer drag suspends-then-resumes; the suspend resumes on every interaction-end signal and self-heals on a track change); which specific `Slider`/pointer events realize it is an implementation choice.

External seeks from other sources (taskbar buttons, SMTC, Next/Previous) arrive as `PositionChanged`; the clock's seek threshold snaps the estimate to the new anchor, so every position source flows through one path.

### Lyrics wiring — pure observer, zero behavior change

`NowPlayingViewModel` only *reads* playback (it maps `NowPlaying`/`Position`/`IsPlaying`/`Duration` and the `TrackChanged`/`PositionChanged`/`PlaybackStateChanged` events into the lyrics-canvas anchors). It therefore injects **`IReadOnlyPlaybackState`** instead of `IPlaybackService`. The values handed to the canvas are byte-for-byte the same, at the same cadence; the canvas keeps doing all smoothing on its own render-thread clock. This is the demonstrating first consumer that earns the new interface its place in v1 — and it proves the observe-only contract on the cleanest surface.

We deliberately do **not** "improve" lyrics under this change (e.g. by having it account for anchor staleness via a timestamp). It already looks right; behavior stays frozen.

---

## Architecture

```
Hypersonic.Core/Contracts/Services/
├── IReadOnlyPlaybackState.cs        (new) NowPlaying, Position, Duration, IsPlaying + 3 events
└── IPlaybackService.cs              now ': IReadOnlyPlaybackState'; read members relocated to the base

Hypersonic.Core/Services/
└── PlaybackClock.cs                 (moved from Hypersonic/Controls/Lyrics/) — behavior unchanged

Hypersonic.Core.Tests/Services/
└── PlaybackClockTests.cs            (moved from Hypersonic.Tests/Controls/Lyrics/)

Hypersonic/Controls/Lyrics/
└── LyricsCanvasControl.xaml.cs      using-only change for PlaybackClock's new namespace

Hypersonic/ViewModels/
├── PlayerViewModel.cs               + PlaybackClock + Stopwatch + ~30fps DispatcherQueueTimer;
│                                      anchor on PositionChanged/PlaybackStateChanged/TrackChanged (→0);
│                                      seconds↔ms at the boundary; per-event one-shot write + tick;
│                                      user-write re-anchor; pointer-drag suspend (fail-safe);
│                                      PositionText updated only on whole-second change
└── NowPlayingViewModel.cs           constructor takes IReadOnlyPlaybackState instead of IPlaybackService

Hypersonic/Views/
└── ShellPage.xaml(.cs)              seek Slider: interaction tracking → suspend/resume the push
                                     (exact event wiring per the implementation doc)

Hypersonic/
└── App.xaml.cs                      register PlaybackService concrete; map both interfaces (was line 144)
```

DI (replaces the single `AddSingleton<IPlaybackService, PlaybackService>()`):

```csharp
services.AddSingleton<PlaybackService>();
services.AddSingleton<IPlaybackService>(sp => sp.GetRequiredService<PlaybackService>());
services.AddSingleton<IReadOnlyPlaybackState>(sp => sp.GetRequiredService<PlaybackService>());
```

`PlaybackService`'s dependencies and internals are unchanged. The only thing that moves is *which interface members are declared where* and *how the singleton is registered*.

---

## Contract: `IReadOnlyPlaybackState` (v1 slice)

```csharp
/// <summary>
/// Read-only observation surface for the playback engine — the query half of the
/// command/query split with <see cref="IPlaybackService"/>. Grown incrementally as
/// pure-observer consumers migrate; today it carries the progress-sync slice.
/// </summary>
public interface IReadOnlyPlaybackState
{
    PlaybackTrack? NowPlaying { get; }
    double Position { get; }            // seconds; the coarse ~500ms anchor
    double Duration { get; }            // seconds
    bool IsPlaying { get; }

    event EventHandler? PlaybackStateChanged;
    event EventHandler<TrackChangedEventArgs>? TrackChanged;
    event EventHandler? PositionChanged;
}

public interface IPlaybackService : IReadOnlyPlaybackState
{
    // unchanged: Queue/QueueIndex/EffectiveQueueIndex/HasNext/HasPrevious/History/Source,
    // Volume/IsMuted/ShuffleEnabled/RepeatMode (read+write), QueueChanged/TrackEnded events,
    // and all command methods (PlayQueue, Resume, Pause, Stop, Next/Previous, Seek,
    // AddNext/AddToEnd/RemoveAt/MoveTrack/Clear*, JumpTo*, GetEffectiveQueue).
}
```

The split axis is **observe vs. command**, not literally read vs. write: the bidirectional properties (`Volume`, `IsMuted`, `ShuffleEnabled`, `RepeatMode`) stay on `IPlaybackService` because a control that both displays and sets them is a *commander*. Only pure-read members belong in the base.

The base is intentionally minimal — just what the progress-sync observers need. `Queue`, `HasNext/Previous`, read-only mode values, etc., migrate into it later, one pure-observer at a time; each migration is its own small, additive diff that breaks nothing (adding a member to the base only obliges the implementer, which we own).

---

## Player-bar position state model

One mediator (`PlayerViewModel`); the clock is anchored by `PositionChanged` / `PlaybackStateChanged` / `TrackChanged`, all units converted seconds↔ms at the boundary:

| Situation | Clock | `Position` writer | Notes |
|---|---|---|---|
| Playing, not interacting | advancing (anchored by 500 ms `PositionChanged`) | the ~30 fps tick (`Estimate()/1000`, external-guarded) | thumb glides; small-delta anchors converge, large-delta snap |
| Track change while playing (Next/Prev) | re-anchored to 0 on `TrackChanged` | tick continues from 0 | prevents racing the old timeline (no `PositionChanged` is fired by `PlayCurrent`) |
| Pointer drag | push **suspended** | the pointer, via two-way binding → `Seek` | suspend must fail safe; existing continuous-scrub behavior preserved |
| Drag end | re-anchored to final pos | resumes ticking | re-anchor (`SetPosition`) + resume on release/capture-lost/cancel |
| Keyboard / gamepad / UIA / track-click | re-anchored to the user value | the bound setter, then tick continues | device-agnostic via `_isExternalUpdate`; no drag, so no suspend needed |
| Paused | frozen | the per-event one-shot write on `PositionChanged` | tick stopped; `Position` otherwise holds the last estimate |
| Stop / seek / Next-Prev while paused | snaps to new anchor | the per-event one-shot write | renders immediately even though no tick follows |
| External seek while playing (taskbar/SMTC) | snaps (>250 ms delta) | per-event write, then tick | arrives as `PositionChanged`; one unified path |

---

## Risk & blast radius

- **Lyrics** — lowest risk: a namespace `using` plus the injected interface type on `NowPlayingViewModel`. No rendering code path changes; values and cadence are identical.
- **Interface split** — mechanical: relocate read members to a base interface and change one DI registration into three lines. Existing `IPlaybackService` consumers compile untouched (members remain reachable through the interface), and the same-object mapping means no behavioral change.
- **Player-bar tick + scrub gating** — the only genuinely new behavior and the one place to get wrong (a tick fighting the cursor, or a stale push during scrub). It is contained to `PlayerViewModel` plus a few lines of `ShellPage` code-behind, and is covered by tests below.

---

## Test strategy

**`PlaybackClockTests`** (moved to `Hypersonic.Core.Tests`) — keep as-is. The controllable `now` lambda already exercises seek-snap vs. smooth-converge and pause-freeze; only the namespace and project change.

**`PlayerViewModel`** (`Hypersonic.Tests`, with an injectable `now` source and a manually-pumpable tick so it stays deterministic):

- **Units:** a `PositionChanged` of *N* seconds, advanced by *M* ms of wall time, yields `Position ≈ N + M/1000` seconds (the seconds↔ms boundary holds; the thumb does not race 1000×).
- While playing, successive ticks advance `Position` *between* 500 ms anchors and never call `Seek`.
- A `PositionChanged` anchor with a small delta converges (no visible jump in the next estimate); a large delta snaps.
- **Track change while playing:** a `TrackChanged` (with the service reporting `Position == 0`, no `PositionChanged`) re-anchors to 0 — a tick right after does not publish the previous track's later time.
- **Stop ordering:** firing `PositionChanged(0)` then `PlaybackStateChanged(false)` (the real `Stop()` order) leaves `Position == 0` — the per-event write lands even though the tick was still nominally running at the position event.
- A `PositionChanged` while paused (a seek/Next-Previous) writes `Position` once from the fresh anchor, even with no tick running.
- **Device-agnostic re-anchor:** a user-originated `Position` set (not from the estimator) issues a `Seek` *and* re-anchors the clock, so the following tick continues from the user's value rather than snapping back.
- **Pointer-drag suspend fails safe:** the push is suspended during a drag and resumes on end; a simulated *missed* end (capture-lost / cancel) still resumes — the suspend can't get stuck.
- `PositionText` changes only when the whole-second value changes.

**Interface / DI** (`Hypersonic.Tests`): resolving `IReadOnlyPlaybackState` and `IPlaybackService` yields the **same** singleton instance.

**`NowPlayingViewModel`** (`Hypersonic.Tests`): existing lyrics-proxy tests pass unchanged against the narrowed dependency (the constructor now takes `IReadOnlyPlaybackState`); fakes implement the read interface.

---

## Deferred / out of scope

- **Growing the read-model.** Migrating `Queue`, read-only modes, and other observers onto `IReadOnlyPlaybackState` — additive, later, per-consumer.
- **Seek-on-release / slider retemplate.** We preserve today's continuous-scrub behavior; changing it is a separate UX decision.
- **A shared progress-bar control or behavior.** Unnecessary for a single slider; `PlaybackClock` is the reused unit, and a future consumer wires its own tick.
- **Central broadcast of a smoothed position.** Rejected by design — interpolation must happen at each consumer's frame time.

---

## References

- Coarse anchor source: `Hypersonic/Services/PlaybackService.cs` — `OnPositionTimerTick` (500 ms timer), `Position`, `Seek`.
- Existing estimator + its consumer: `Hypersonic/Controls/Lyrics/PlaybackClock.cs`, `LyricsCanvasControl.xaml.cs` (`() => _frameNowMs` Win2D feed).
- Lyrics observer: `Hypersonic/ViewModels/NowPlayingViewModel.cs` (`SyncPlaybackProxies`).
- Player mediator + bound slider: `Hypersonic/ViewModels/PlayerViewModel.cs`, `Hypersonic/Views/ShellPage.xaml` (seek `Slider`).
- Interface + DI: `Hypersonic.Core/Contracts/Services/IPlaybackService.cs`, `Hypersonic/App.xaml.cs`.
- Pure-logic-in-Core precedent: `Hypersonic.Core/Services/PlaybackQueue.cs`.
