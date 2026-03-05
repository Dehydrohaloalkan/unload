using Unload.Core;

namespace Unload.Application;

public interface IRunCoordinator
{
    bool TryActivate(RunRequest request);

    IAsyncEnumerable<RunRequest> ReadActivationsAsync(CancellationToken cancellationToken);

    void Complete(string correlationId);

    string? GetActiveCorrelationId();
}
