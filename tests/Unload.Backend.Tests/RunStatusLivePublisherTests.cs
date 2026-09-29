using Microsoft.Extensions.Logging.Abstractions;
using Unload.Api.Services;
using Unload.Store;

namespace Unload.Backend.Tests;

public sealed class RunStatusLivePublisherTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PublishAsync_SendsSnapshotImmediately()
    {
        var transport = new CapturingTransport();
        var publisher = CreatePublisher(transport);

        await publisher.PublishAsync(State(1));

        Assert.Equal(State(1), Assert.Single(transport.Sent));
    }

    [Fact]
    public async Task OlderSnapshot_DoesNotRegressPublishedState()
    {
        var transport = new CapturingTransport();
        var publisher = CreatePublisher(transport);

        await publisher.PublishAsync(State(2));
        await publisher.PublishAsync(State(1));

        Assert.Equal(State(2), Assert.Single(transport.Sent));
    }

    [Fact]
    public async Task TerminalSnapshot_DropsLateUpdates()
    {
        var transport = new CapturingTransport();
        var publisher = CreatePublisher(transport);

        await publisher.PublishAsync(State(1, RunLifecycleStatus.Completed));
        await publisher.PublishAsync(State(2));

        Assert.Equal(RunLifecycleStatus.Completed, Assert.Single(transport.Sent).Status);
    }

    [Fact]
    public async Task RetentionIsBounded_AndKeepsRecentTerminalGuard()
    {
        var transport = new CapturingTransport();
        var publisher = CreatePublisher(transport);
        for (var index = 0; index <= RunStatusLivePublisher.RetainedRunCapacity; index++)
        {
            await publisher.PublishAsync(State(1, RunLifecycleStatus.Completed, $"run-{index}"));
        }

        await publisher.PublishAsync(State(2, correlationId: "run-1"));
        await publisher.PublishAsync(State(2, correlationId: "run-0"));

        Assert.Equal(RunStatusLivePublisher.RetainedRunCapacity + 2, transport.Sent.Count);
        Assert.Equal(2, transport.Sent.Count(state => state.CorrelationId == "run-0"));
        Assert.Single(transport.Sent, state => state.CorrelationId == "run-1");
    }

    [Fact]
    public async Task TransportFailure_DoesNotEscapeOrRetry()
    {
        var transport = new FailingTransport();
        var publisher = CreatePublisher(transport);

        await publisher.PublishAsync(State(1));

        Assert.Equal(1, transport.Attempts);
    }

    private static RunStatusLivePublisher CreatePublisher(IRunStatusLiveTransport transport) =>
        new(transport, NullLogger<RunStatusLivePublisher>.Instance);

    private static RunStatusInfo State(
        int revision,
        RunLifecycleStatus status = RunLifecycleStatus.Running,
        string correlationId = "run-1") =>
        new(correlationId, "run", status, [], StartedAt, StartedAt.AddSeconds(revision));

    private sealed class CapturingTransport : IRunStatusLiveTransport
    {
        public List<RunStatusInfo> Sent { get; } = [];

        public Task SendAsync(RunStatusInfo state, CancellationToken cancellationToken)
        {
            Sent.Add(state);
            return Task.CompletedTask;
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
}
