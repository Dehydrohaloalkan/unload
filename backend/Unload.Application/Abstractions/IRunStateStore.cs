using Unload.Core;

namespace Unload.Application;

public interface IRunStateStore
{
    void SetQueued(string correlationId, IReadOnlyCollection<string> profileCodes);

    void SetRunning(string correlationId);

    void ApplyEvent(RunnerEvent @event);

    void SetFailed(string correlationId, string message);

    RunStatusInfo? Get(string correlationId);

    IReadOnlyList<RunStatusInfo> List();
}
