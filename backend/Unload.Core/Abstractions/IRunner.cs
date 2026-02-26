namespace Unload.Core;

public interface IRunner
{
    IAsyncEnumerable<RunnerEvent> RunAsync(RunRequest request, CancellationToken cancellationToken);
}
