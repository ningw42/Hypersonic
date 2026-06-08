using CommunityToolkit.Mvvm.Messaging;

using Hypersonic.Client;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Messages;

using Microsoft.UI.Dispatching;

namespace Hypersonic.Services;

/// <summary>
/// The single writer of the app's in-memory starred-song set. Owns the optimistic flip +
/// <c>Star/Unstar</c> API call + status check + revert in exactly one place, and broadcasts
/// every change as a batched <see cref="FavoritesChangedMessage"/>. Fed by three inputs —
/// user toggles, a background <c>getStarred2</c> poll, and page-load reconciles — all
/// serialized through one epoch/seq/lease write gate. See
/// <c>docs/design/service/2026-06-07-favorite-sync.md</c>.
///
/// All members run on the UI thread, so the store needs no lock. The one exception is the
/// broadcast dispatch rule, which sends inline when it already has UI-thread access (so the
/// clicked row updates reentrantly) and enqueues only for a stray off-thread caller.
/// </summary>
public sealed class FavoritesService : IFavoritesService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly ISubsonicClientService _clientService;
    private readonly IMessenger _messenger;
    private readonly ILogger _log;

    // The store: current known starred ids (render-coordination truth).
    private readonly HashSet<string> _starred = new();

    // Write gate.
    private long _epoch;                                          // connection lifetime
    private long _seq;                                            // globally monotonic read/write order
    private readonly Dictionary<string, long> _lastWriteSeq = new();
    private long _lastFullRefreshSeq;                             // absence floor
    private readonly Dictionary<string, bool> _desired = new();   // per-id toggle lease (presence ⇒ leased)
    private RefreshLease? _refresh;                               // refresh lease

    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _timer;
    private bool _started;

    public FavoritesService(
        ISubsonicClientService clientService,
        ILoggingService logService,
        IMessenger messenger)
    {
        _clientService = clientService;
        _messenger = messenger;
        _log = logService.GetLogger(nameof(FavoritesService));
    }

    public bool IsStarred(string songId) => _starred.Contains(songId);

    public OperationStamp BeginObservation() => new(_epoch, ++_seq);

    public async Task ToggleAsync(string songId, bool desired)
    {
        var client = _clientService.Client;
        if (client is null)
        {
            return; // disconnected — no-op before any optimistic change (no flicker)
        }

        // Store==server when no op is in flight for this id; capture before mutating.
        var serverKnown = _starred.Contains(songId);

        // Optimistic apply, store-first, then broadcast (reentrant on the UI thread).
        ApplyLocal(songId, desired);
        var alreadyLeased = _desired.ContainsKey(songId);
        _desired[songId] = desired;
        Broadcast(desired, songId);

        if (alreadyLeased)
        {
            return; // a worker already owns this id; we only recorded the new intent
        }

        // Become the single worker for this id.
        var epoch = _epoch;

        while (true)
        {
            var target = _desired[songId];
            if (target == serverKnown)
            {
                // Latest intent already matches the server — settle without a call.
                if (_epoch == epoch)
                {
                    _desired.Remove(songId);
                }
                return;
            }

            var failed = false;
            try
            {
                var response = target
                    ? await client.StarAsync(id: songId)
                    : await client.UnstarAsync(id: songId);

                if (_epoch != epoch)
                {
                    return; // replaced connection — discard wholesale; never touch _desired
                }

                if (response.Status == "failed")
                {
                    failed = true;
                    _log.Warning(
                        "Star toggle returned a failed status",
                        new { SongId = songId, Target = target, Code = response.Error?.Code, Message = response.Error?.Message });
                }
            }
            catch (Exception ex)
            {
                if (_epoch != epoch)
                {
                    return;
                }

                failed = true;
                _log.Warning("Failed to toggle star", new { SongId = songId, Target = target }, ex);
            }

            if (!failed)
            {
                serverKnown = target;
                ++_seq;
                _lastWriteSeq[songId] = _seq;   // stamp on confirmation so a stale refresh can't regress it
                continue;
            }

            // Failure: only revert if no newer intent superseded the failed target.
            if (_desired.TryGetValue(songId, out var current) && current == target)
            {
                ApplyLocal(songId, serverKnown);
                Broadcast(serverKnown, songId);
                _desired.Remove(songId);
                return;
            }

            // A newer intent arrived during the call — loop and re-evaluate against serverKnown.
        }
    }

    public Task RefreshAsync()
    {
        var client = _clientService.Client;
        if (client is null)
        {
            return Task.CompletedTask;
        }

        if (_refresh is { } held && held.Epoch == _epoch)
        {
            held.Queued = true;       // coalesce into one trailing pass
            return held.Task;
        }

        var lease = new RefreshLease { Epoch = _epoch };
        _refresh = lease;
        lease.Task = RunRefreshAsync(client, lease);
        return lease.Task;
    }

    private async Task RunRefreshAsync(ISubsonicClient client, RefreshLease lease)
    {
        var epoch = lease.Epoch;
        try
        {
            var obsSeq = ++_seq;       // captured at fetch start

            GetStarred2Response response;
            try
            {
                response = await client.GetStarred2Async();
            }
            catch (Exception ex)
            {
                _log.Warning("getStarred2 failed; favorites store left untouched", exception: ex);
                return;
            }

            if (_epoch != epoch)
            {
                return; // replaced connection — discard
            }

            if (response.Status == "failed")
            {
                _log.Warning("getStarred2 returned a failed status", new { Code = response.Error?.Code });
                return;
            }

            var serverIds = new HashSet<string>(
                response.Starred2?.Song?.Select(s => s.Id) ?? Enumerable.Empty<string>());

            var (added, removed, survivors) = ComputeDiff(
                serverIds, _starred,
                skip: id => _desired.ContainsKey(id) || EffectiveSeq(id) > obsSeq);

            // Apply store + stamps first so a synchronous recipient already sees the new value.
            foreach (var id in survivors)
            {
                _lastWriteSeq[id] = obsSeq;     // stamp even on a no-diff confirmation
            }
            foreach (var id in added)
            {
                _starred.Add(id);
            }
            foreach (var id in removed)
            {
                _starred.Remove(id);
            }

            _lastFullRefreshSeq = Math.Max(_lastFullRefreshSeq, obsSeq);   // floor versions every absent id
            PruneWriteSeqAtOrBelowFloor();

            Broadcast(true, added);
            Broadcast(false, removed);
        }
        finally
        {
            // Only the live (same-epoch) lease may clear itself and start the trailing pass.
            if (ReferenceEquals(_refresh, lease))
            {
                _refresh = null;
                if (lease.Queued)
                {
                    lease.Queued = false;
                    _ = RefreshAsync();   // exactly one trailing pass guarantees eventual freshness
                }
            }
        }
    }

    public void Reconcile(IEnumerable<(string Id, bool Starred)> observed, OperationStamp since)
    {
        if (since.Epoch != _epoch)
        {
            return; // stale connection — ignore the whole observation
        }

        var added = new List<string>();
        var removed = new List<string>();

        foreach (var (id, starred) in observed)
        {
            if (_desired.ContainsKey(id))
            {
                continue; // leased — protect the in-flight toggle
            }

            if (EffectiveSeq(id) > since.Seq)
            {
                continue; // a newer write already owns this id
            }

            _lastWriteSeq[id] = since.Seq;     // stamp even on a no-diff confirmation
            var isNow = _starred.Contains(id);
            if (starred == isNow)
            {
                continue;
            }

            if (starred)
            {
                _starred.Add(id);
                added.Add(id);
            }
            else
            {
                _starred.Remove(id);
                removed.Add(id);
            }
        }

        // A page is a partial read — it never raises _lastFullRefreshSeq.
        Broadcast(true, added);
        Broadcast(false, removed);
    }

    public void Start()
    {
        _epoch++;
        _starred.Clear();
        _desired.Clear();
        _lastWriteSeq.Clear();
        _lastFullRefreshSeq = 0;
        // _seq stays globally monotonic across epochs — never reset.

        if (!_started)
        {
            _started = true;
            try
            {
                _dispatcher = DispatcherQueue.GetForCurrentThread();   // throws/null off the UI thread (tests)
                if (_dispatcher is not null)
                {
                    _timer = _dispatcher.CreateTimer();
                    _timer.Interval = PollInterval;
                    _timer.IsRepeating = true;
                    _timer.Tick += (_, _) => _ = RefreshAsync();
                    _timer.Start();
                }
            }
            catch (Exception ex)
            {
                // No dispatcher on this thread (e.g. a unit-test thread) — run without the
                // background poll timer; broadcasts then send inline. Production always has one.
                _dispatcher = null;
                _log.Debug("No dispatcher available; favorites poll timer disabled", new { Error = ex.Message });
            }
        }

        _ = RefreshAsync();   // seed under the new epoch (installs a fresh lease)
    }

    /// <summary>
    /// Pure diff for direct unit testing: from the server's starred set and the current
    /// store, with a <paramref name="skip"/> predicate (leased or newer-versioned ids),
    /// computes the ids to add, to remove, and all surviving (non-skipped) ids.
    /// </summary>
    internal static (List<string> Added, List<string> Removed, List<string> Survivors) ComputeDiff(
        IReadOnlyCollection<string> serverIds,
        IReadOnlySet<string> currentStarred,
        Func<string, bool> skip)
    {
        var server = serverIds as IReadOnlySet<string> ?? new HashSet<string>(serverIds);
        var added = new List<string>();
        var removed = new List<string>();
        var survivors = new List<string>();
        var seen = new HashSet<string>();

        foreach (var id in server.Concat(currentStarred))
        {
            if (!seen.Add(id))
            {
                continue; // dedupe the union
            }

            if (skip(id))
            {
                continue;
            }

            survivors.Add(id);
            var shouldBe = server.Contains(id);
            var isNow = currentStarred.Contains(id);
            if (shouldBe && !isNow)
            {
                added.Add(id);
            }
            else if (!shouldBe && isNow)
            {
                removed.Add(id);
            }
        }

        return (added, removed, survivors);
    }

    private void ApplyLocal(string id, bool starred)
    {
        ++_seq;
        _lastWriteSeq[id] = _seq;
        if (starred)
        {
            _starred.Add(id);
        }
        else
        {
            _starred.Remove(id);
        }
    }

    private long EffectiveSeq(string id)
        => Math.Max(_lastWriteSeq.TryGetValue(id, out var s) ? s : 0, _lastFullRefreshSeq);

    private void PruneWriteSeqAtOrBelowFloor()
    {
        if (_lastFullRefreshSeq == 0)
        {
            return;
        }

        // Entries at or below the floor are redundant: effectiveSeq folds the floor in.
        var stale = _lastWriteSeq.Where(kv => kv.Value <= _lastFullRefreshSeq).Select(kv => kv.Key).ToList();
        foreach (var id in stale)
        {
            _lastWriteSeq.Remove(id);
        }
    }

    private void Broadcast(bool isStarred, string id) => Broadcast(isStarred, new[] { id });

    private void Broadcast(bool isStarred, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var message = new FavoritesChangedMessage(isStarred, ids);
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            _messenger.Send(message);                       // inline → reentrant, instant clicked-row update
        }
        else
        {
            _dispatcher.TryEnqueue(() => _messenger.Send(message));   // defensive off-thread path only
        }
    }

    private sealed class RefreshLease
    {
        public long Epoch;
        public Task Task = Task.CompletedTask;
        public bool Queued;
    }
}
