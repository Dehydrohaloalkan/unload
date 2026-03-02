namespace Unload.Api;

public interface IRunStateStore
{
    void SetQueued(string correlationId, IReadOnlyCollection<string> profileCodes);

    void SetRunning(string correlationId);

    void ApplyEvent(Unload.Core.RunnerEvent @event);

    void SetFailed(string correlationId, string message);

    RunStatusInfo? Get(string correlationId);

    IReadOnlyList<RunStatusInfo> List();
}
