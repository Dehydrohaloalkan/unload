using Unload.Core;

namespace Unload.Api;

public interface IRunQueue
{
    bool TryEnqueue(RunRequest request);

    IAsyncEnumerable<RunRequest> DequeueAllAsync(CancellationToken cancellationToken);
}
