using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Unload.Api.Services;
using Unload.Store;

namespace Unload.Backend.Tests;

public sealed class RunStatusLivePublisherTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BurstForOneRun_PublishesOnlyLatestSnapshotAfterWindow()
    {
        var transport = new CapturingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);

        await publisher.PublishAsync(State("run-1", 1));
        await publisher.PublishAsync(State("run-1", 2));
        await publisher.PublishAsync(State("run-1", 3));

        Assert.Empty(transport.Sent);
        delay.ReleaseAll();
        await transport.WaitForCountAsync(1);

        var sent = Assert.Single(transport.Sent);
        Assert.Equal(StartedAt.AddSeconds(3), sent.UpdatedAt);
    }

    [Fact]
    public async Task TerminalSnapshot_IsPublishedImmediately_AndSupersedesDelayedSnapshot()
    {
        var transport = new CapturingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);

        await publisher.PublishAsync(State("run-1", 1));
        await publisher.PublishAsync(State("run-1", 2, RunLifecycleStatus.Completed));

        var sent = Assert.Single(transport.Sent);
        Assert.Equal(RunLifecycleStatus.Completed, sent.Status);

        delay.ReleaseAll();
        await Task.Yield();
        Assert.Single(transport.Sent);
        Assert.Equal(0, publisher.TrackedRunCount);
    }

    [Fact]
    public async Task DifferentRuns_HaveIndependentCoalescingWindows()
    {
        var transport = new CapturingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);

        await publisher.PublishAsync(State("run-1", 1));
        await publisher.PublishAsync(State("run-2", 1));
        await publisher.PublishAsync(State("run-1", 2));

        Assert.Equal(2, delay.PendingCount);
        delay.ReleaseAll();
        await transport.WaitForCountAsync(2);

        Assert.Equal(2, transport.Sent.Count);
        Assert.Contains(transport.Sent, state => state.CorrelationId == "run-1" && state.UpdatedAt == StartedAt.AddSeconds(2));
        Assert.Contains(transport.Sent, state => state.CorrelationId == "run-2" && state.UpdatedAt == StartedAt.AddSeconds(1));
    }

    [Fact]
    public async Task OlderLateSnapshot_CannotOverwriteNewerPublishedSnapshot()
    {
        var transport = new CapturingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);

        await publisher.PublishAsync(State("run-1", 3), immediate: true);
        await publisher.PublishAsync(State("run-1", 1), immediate: true);
        await publisher.PublishAsync(State("run-1", 2));

        delay.ReleaseAll();
        await Task.Yield();

        var sent = Assert.Single(transport.Sent);
        Assert.Equal(StartedAt.AddSeconds(3), sent.UpdatedAt);
    }

    [Fact]
    public async Task EqualTimestampAndSequence_PrefersLastAcceptedSnapshot()
    {
        var transport = new CapturingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);
        var first = State("run-1", 1) with { Message = "old" };
        var second = first with { Message = "new" };

        await publisher.PublishAsync(first);
        await publisher.PublishAsync(second);
        delay.ReleaseAll();
        await transport.WaitForCountAsync(1);

        Assert.Equal("new", Assert.Single(transport.Sent).Message);
    }

    [Fact]
    public async Task Stop_FlushesPendingLatestSnapshot()
    {
        var transport = new CapturingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);

        await publisher.PublishAsync(State("run-1", 1));
        await publisher.PublishAsync(State("run-1", 2));
        await publisher.StopAsync(CancellationToken.None);

        var sent = Assert.Single(transport.Sent);
        Assert.Equal(StartedAt.AddSeconds(2), sent.UpdatedAt);
    }

    [Fact]
    public async Task TransportFailure_IsDroppedWithoutRetryStorm()
    {
        var transport = new FailingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);

        await publisher.PublishAsync(State("run-1", 1), immediate: true);
        delay.ReleaseAll();
        await Task.Yield();

        Assert.Equal(1, transport.Attempts);
        Assert.Equal(0, delay.PendingCount);
    }

    [Fact]
    public async Task NormalSend_UsesPublisherLifetimeCancellation()
    {
        var transport = new CapturingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);

        await publisher.PublishAsync(State("run-1", 1), immediate: true);

        Assert.True(transport.LastCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task Stop_HonorsCancellation_WhenTransportDoesNotComplete()
    {
        var transport = new HangingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);
        await publisher.PublishAsync(State("run-1", 1));
        using var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await publisher.StopAsync(stopCts.Token).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task TerminalSend_ReleasesSnapshots_AndDropsLateUpdates()
    {
        var transport = new CapturingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);

        await publisher.PublishAsync(State("run-1", 1));
        await publisher.PublishAsync(State("run-1", 2, RunLifecycleStatus.Completed));
        Assert.Equal(0, publisher.TrackedRunCount);
        Assert.Equal(0, publisher.TrackedSnapshotReferenceCount);
        Assert.Equal(1, publisher.TerminalTombstoneCount);

        delay.ReleaseAll();
        await publisher.PublishAsync(State("run-1", 3), immediate: true);
        await publisher.PublishAsync(State("run-1", 3, RunLifecycleStatus.Completed));

        Assert.Equal(0, publisher.TrackedRunCount);
        Assert.Equal(0, publisher.TrackedSnapshotReferenceCount);
        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task TerminalTombstones_AreBoundedAndEvictOldestDeterministically()
    {
        var transport = new CountingTransport();
        var delay = new ControlledDelay();
        await using var publisher = CreatePublisher(transport, delay);

        for (var index = 0; index <= RunStatusLivePublisher.TerminalTombstoneCapacity; index++)
        {
            await publisher.PublishAsync(State($"run-{index}", 1, RunLifecycleStatus.Completed));
        }

        Assert.Equal(RunStatusLivePublisher.TerminalTombstoneCapacity, publisher.TerminalTombstoneCount);
        Assert.Equal(RunStatusLivePublisher.TerminalTombstoneCapacity + 1, transport.Attempts);

        await publisher.PublishAsync(State("run-0", 2), immediate: true);
        await publisher.PublishAsync(State("run-1", 2), immediate: true);

        Assert.Equal(RunStatusLivePublisher.TerminalTombstoneCapacity + 2, transport.Attempts);
        Assert.Equal(1, publisher.TrackedRunCount);
    }

    private static RunStatusLivePublisher CreatePublisher(
        IRunStatusLiveTransport transport,
        IRunStatusPublishDelay delay) =>
        new(transport, delay, NullLogger<RunStatusLivePublisher>.Instance);

    private static RunStatusInfo State(
        string correlationId,
        int revision,
        RunLifecycleStatus status = RunLifecycleStatus.Running) =>
        new(
            correlationId,
            "run",
            status,
            [],
            StartedAt,
            StartedAt.AddSeconds(revision));

    private sealed class ControlledDelay : IRunStatusPublishDelay
    {
        private readonly ConcurrentQueue<TaskCompletionSource> _pending = new();

        public int PendingCount => _pending.Count;

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Assert.Equal(RunStatusLivePublisher.CoalescingWindow, delay);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            _pending.Enqueue(completion);
            return completion.Task;
        }

        public void ReleaseAll()
        {
            while (_pending.TryDequeue(out var completion))
            {
                completion.TrySetResult();
            }
        }
    }

    private sealed class CapturingTransport : IRunStatusLiveTransport
    {
        private readonly ConcurrentQueue<RunStatusInfo> _sent = new();
        private readonly SemaphoreSlim _signal = new(0);

        public IReadOnlyCollection<RunStatusInfo> Sent => _sent.ToArray();
        public CancellationToken LastCancellationToken { get; private set; }

        public Task SendAsync(RunStatusInfo state, CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            _sent.Enqueue(state);
            _signal.Release();
            return Task.CompletedTask;
        }

        public async Task WaitForCountAsync(int expected)
        {
            while (_sent.Count < expected)
            {
                await _signal.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
    }

    private sealed class FailingTransport : IRunStatusLiveTransport
    {
        public int Attempts { get; private set; }

        public Task SendAsync(RunStatusInfo state, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new InvalidOperationException("SignalR is unavailable.");
        }
    }

    private sealed class HangingTransport : IRunStatusLiveTransport
    {
        public int Attempts { get; private set; }

        public Task SendAsync(RunStatusInfo state, CancellationToken cancellationToken)
        {
            Attempts++;
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }

    private sealed class CountingTransport : IRunStatusLiveTransport
    {
        public int Attempts { get; private set; }

        public Task SendAsync(RunStatusInfo state, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.CompletedTask;
        }
    }
}
