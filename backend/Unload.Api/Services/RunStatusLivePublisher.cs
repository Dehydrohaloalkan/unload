using Microsoft.AspNetCore.SignalR;
using Unload.Store;

namespace Unload.Api.Services;

public interface IRunStatusLiveTransport
{
    Task SendAsync(RunStatusInfo state, CancellationToken cancellationToken);
}

public sealed class SignalRRunStatusLiveTransport(IHubContext<RunStatusHub> hubContext)
    : IRunStatusLiveTransport
{
    public Task SendAsync(RunStatusInfo state, CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendRunStatusAsync(state, cancellationToken);
}

public interface IRunStatusPublishDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class RunStatusPublishDelay : IRunStatusPublishDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

/// <summary>
/// Serializes and coalesces large live run snapshots. REST reads and persistence remain unthrottled.
/// </summary>
public sealed class RunStatusLivePublisher(
    IRunStatusLiveTransport transport,
    IRunStatusPublishDelay delay,
    ILogger<RunStatusLivePublisher> logger) : IHostedService, IAsyncDisposable
{
    public static readonly TimeSpan CoalescingWindow = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(2);
    internal const int TerminalTombstoneCapacity = 1_024;

    private readonly object _gate = new();
    private readonly Dictionary<string, RunEntry> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _terminalTombstones = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _terminalTombstoneOrder = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationTokenSource _delays = new();
    private long _nextOrder;
    private bool _accepting = true;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishAsync(RunStatusInfo state, bool immediate = false) =>
        EnqueueAsync(state, immediate);

    internal int TrackedRunCount
    {
        get
        {
            lock (_gate)
            {
                return _runs.Count;
            }
        }
    }

    internal int TerminalTombstoneCount
    {
        get
        {
            lock (_gate)
            {
                return _terminalTombstones.Count;
            }
        }
    }

    internal int TrackedSnapshotReferenceCount
    {
        get
        {
            lock (_gate)
            {
                return _runs.Values.Sum(static entry =>
                    (entry.Pending is null ? 0 : 1) +
                    (entry.LatestAccepted is null ? 0 : 1) +
                    (entry.LastSent is null ? 0 : 1));
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        List<PendingSnapshot> pending;
        List<Task> delayedPublishes;
        lock (_gate)
        {
            _accepting = false;
            pending = _runs.Values
                .Select(static entry => entry.Pending)
                .Where(static state => state is not null)
                .Cast<PendingSnapshot>()
                .ToList();
            delayedPublishes = _runs.Values
                .Select(static entry => entry.DelayTask)
                .Where(static task => task is not null)
                .Cast<Task>()
                .ToList();

            foreach (var entry in _runs.Values)
            {
                entry.Pending = null;
            }
        }

        _delays.Cancel();
        using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        flushCts.CancelAfter(ShutdownFlushTimeout);
        try
        {
            foreach (var state in pending)
            {
                await SendLatestAsync(state, flushCts.Token).WaitAsync(flushCts.Token);
            }

            await Task.WhenAll(delayedPublishes).WaitAsync(flushCts.Token);
        }
        catch (OperationCanceledException) when (flushCts.IsCancellationRequested)
        {
            logger.LogWarning("Live run snapshot flush was cancelled or exceeded {TimeoutMs} ms.", ShutdownFlushTimeout.TotalMilliseconds);
        }
        finally
        {
            _lifetime.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_accepting)
        {
            await StopAsync(CancellationToken.None);
        }

        // Do not dispose synchronization primitives here: an uncooperative transport may finish
        // after the bounded shutdown wait. The process is stopping and the lifetime token is cancelled.
    }

    private Task EnqueueAsync(RunStatusInfo state, bool immediate)
    {
        PendingSnapshot? candidate = null;
        lock (_gate)
        {
            if (!_accepting)
            {
                return Task.CompletedTask;
            }

            if (_terminalTombstones.Contains(state.CorrelationId))
            {
                return Task.CompletedTask;
            }

            if (!_runs.TryGetValue(state.CorrelationId, out var entry))
            {
                entry = new RunEntry();
                _runs.Add(state.CorrelationId, entry);
            }

            if (entry.TerminalAccepted)
            {
                return Task.CompletedTask;
            }

            if (entry.LatestAccepted is not null && Compare(state, entry.LatestAccepted.State) < 0)
            {
                return Task.CompletedTask;
            }

            immediate |= RequiresImmediatePublish(state);
            var snapshot = new PendingSnapshot(state, ++_nextOrder, entry);
            entry.LatestAccepted = snapshot;
            entry.Pending = snapshot;
            entry.TerminalAccepted = IsTerminal(state);

            if (immediate)
            {
                candidate = entry.Pending;
                entry.Pending = null;
            }
            else if (entry.DelayTask is null || entry.DelayTask.IsCompleted)
            {
                entry.DelayTask = PublishAfterWindowAsync(state.CorrelationId);
            }
        }

        return candidate is null ? Task.CompletedTask : SendLatestAsync(candidate, _lifetime.Token);
    }

    private async Task PublishAfterWindowAsync(string correlationId)
    {
        while (true)
        {
            try
            {
                await delay.WaitAsync(CoalescingWindow, _delays.Token);
            }
            catch (OperationCanceledException) when (_delays.IsCancellationRequested)
            {
                return;
            }

            PendingSnapshot? candidate;
            lock (_gate)
            {
                if (!_runs.TryGetValue(correlationId, out var entry) || entry.Closed)
                {
                    return;
                }

                candidate = entry.Pending;
                entry.Pending = null;
                if (candidate is null)
                {
                    entry.DelayTask = null;
                    return;
                }
            }

            await SendLatestAsync(candidate, _lifetime.Token);

            lock (_gate)
            {
                if (!_runs.TryGetValue(correlationId, out var entry) ||
                    !ReferenceEquals(entry, candidate.Entry) ||
                    entry.Closed)
                {
                    return;
                }

                if (entry.Pending is null)
                {
                    entry.DelayTask = null;
                    return;
                }
            }
        }
    }

    private async Task SendLatestAsync(PendingSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            await _sendLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupTerminal(snapshot);
            return;
        }

        try
        {
            lock (_gate)
            {
                if (!_runs.TryGetValue(snapshot.State.CorrelationId, out var entry) ||
                    !ReferenceEquals(entry, snapshot.Entry) ||
                    entry.Closed)
                {
                    return;
                }

                if (entry.TerminalSent ||
                    entry.LastSent is not null && IsNotNewer(snapshot, entry.LastSent))
                {
                    return;
                }
            }

            try
            {
                await transport.SendAsync(snapshot.State, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish live run snapshot. CorrelationId: {CorrelationId}", snapshot.State.CorrelationId);
                return;
            }

            lock (_gate)
            {
                if (!_runs.TryGetValue(snapshot.State.CorrelationId, out var entry) ||
                    !ReferenceEquals(entry, snapshot.Entry) ||
                    entry.Closed)
                {
                    return;
                }

                if (IsTerminal(snapshot.State))
                {
                    entry.TerminalSent = true;
                }
                else if (entry.LastSent is null || !IsNotNewer(snapshot, entry.LastSent))
                {
                    entry.LastSent = snapshot;
                }
            }
        }
        finally
        {
            _sendLock.Release();
            CleanupTerminal(snapshot);
        }
    }

    private void CleanupTerminal(PendingSnapshot snapshot)
    {
        if (!IsTerminal(snapshot.State))
        {
            return;
        }

        lock (_gate)
        {
            if (_runs.TryGetValue(snapshot.State.CorrelationId, out var entry) &&
                ReferenceEquals(entry, snapshot.Entry))
            {
                entry.Closed = true;
                entry.Pending = null;
                entry.LatestAccepted = null;
                entry.LastSent = null;
                _runs.Remove(snapshot.State.CorrelationId);
                AddTerminalTombstone(snapshot.State.CorrelationId);
            }
        }
    }

    private void AddTerminalTombstone(string correlationId)
    {
        if (!_terminalTombstones.Add(correlationId))
        {
            return;
        }

        _terminalTombstoneOrder.Enqueue(correlationId);
        while (_terminalTombstoneOrder.Count > TerminalTombstoneCapacity)
        {
            _terminalTombstones.Remove(_terminalTombstoneOrder.Dequeue());
        }
    }

    private static bool RequiresImmediatePublish(RunStatusInfo state) =>
        IsTerminal(state) || state.Status == RunLifecycleStatus.CancellationRequested;

    private static bool IsTerminal(RunStatusInfo state) =>
        state.Status is RunLifecycleStatus.Completed
            or RunLifecycleStatus.Failed
            or RunLifecycleStatus.Cancelled;

    private static int Compare(RunStatusInfo left, RunStatusInfo right)
    {
        var timestamp = left.UpdatedAt.CompareTo(right.UpdatedAt);
        return timestamp != 0 ? timestamp : MaxSequence(left).CompareTo(MaxSequence(right));
    }

    private static bool IsNotNewer(PendingSnapshot candidate, PendingSnapshot baseline)
    {
        var comparison = Compare(candidate.State, baseline.State);
        return comparison < 0 || comparison == 0 && candidate.Order <= baseline.Order;
    }

    private static long MaxSequence(RunStatusInfo state)
    {
        var result = 0L;
        if (state.MemberStatuses is not null)
        {
            result = Math.Max(result, state.MemberStatuses.Values.Select(static item => item.Sequence ?? 0).DefaultIfEmpty().Max());
        }

        if (state.WorkerStatuses is not null)
        {
            result = Math.Max(result, state.WorkerStatuses.Values.Select(static item => item.Sequence ?? 0).DefaultIfEmpty().Max());
        }

        if (state.ScriptStatuses is not null)
        {
            result = Math.Max(result, state.ScriptStatuses.Values.Select(static item => item.Sequence ?? 0).DefaultIfEmpty().Max());
        }

        if (state.FileStatuses is not null)
        {
            result = Math.Max(result, state.FileStatuses.Values.Select(static item => item.Sequence ?? 0).DefaultIfEmpty().Max());
        }

        if (state.SenderBatches is not null)
        {
            result = Math.Max(result, state.SenderBatches.Values.Select(static item => item.Sequence ?? 0).DefaultIfEmpty().Max());
        }

        return result;
    }

    private sealed class RunEntry
    {
        public PendingSnapshot? Pending { get; set; }
        public PendingSnapshot? LatestAccepted { get; set; }
        public PendingSnapshot? LastSent { get; set; }
        public Task? DelayTask { get; set; }
        public bool TerminalAccepted { get; set; }
        public bool TerminalSent { get; set; }
        public bool Closed { get; set; }
    }

    private sealed record PendingSnapshot(RunStatusInfo State, long Order, RunEntry Entry);
}
