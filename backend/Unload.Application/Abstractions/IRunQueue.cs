using Unload.Core;

namespace Unload.Application;

public interface IRunQueue
{
    bool TryEnqueue(RunRequest request);

    IAsyncEnumerable<RunRequest> DequeueAllAsync(CancellationToken cancellationToken);
}
