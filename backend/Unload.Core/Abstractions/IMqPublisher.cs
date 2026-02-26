namespace Unload.Core;

public interface IMqPublisher
{
    Task PublishAsync(RunnerEvent @event, CancellationToken cancellationToken);
}
