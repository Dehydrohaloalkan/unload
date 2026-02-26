using System.Collections.Concurrent;
using Unload.Core;

namespace Unload.MQ;

public sealed class InMemoryMqPublisher : IMqPublisher
{
    private readonly ConcurrentQueue<RunnerEvent> _events = new();

    public Task PublishAsync(RunnerEvent @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Enqueue(@event);
        return Task.CompletedTask;
    }
}
