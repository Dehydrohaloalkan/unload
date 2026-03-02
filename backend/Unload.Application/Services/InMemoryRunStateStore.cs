using System.Collections.Concurrent;
using Unload.Core;

namespace Unload.Application;

public class InMemoryRunStateStore : IRunStateStore
{
    private readonly ConcurrentDictionary<string, RunStatusInfo> _runs = new(StringComparer.OrdinalIgnoreCase);

    public void SetQueued(string correlationId, IReadOnlyCollection<string> profileCodes)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new RunStatusInfo(
            correlationId,
            RunLifecycleStatus.Queued,
            profileCodes.ToArray(),
            now,
            now,
            Message: "Run queued.");

        _runs[correlationId] = snapshot;
    }

    public void SetRunning(string correlationId)
    {
        var now = DateTimeOffset.UtcNow;
        _runs.AddOrUpdate(
            correlationId,
            _ => new RunStatusInfo(
                correlationId,
                RunLifecycleStatus.Running,
                Array.Empty<string>(),
                now,
                now,
                Message: "Run started."),
            (_, current) => current with
            {
                Status = RunLifecycleStatus.Running,
                UpdatedAt = now,
                Message = "Run started."
            });
    }

    public void ApplyEvent(RunnerEvent @event)
    {
        var now = DateTimeOffset.UtcNow;
        _runs.AddOrUpdate(
            @event.CorrelationId,
            _ => new RunStatusInfo(
                @event.CorrelationId,
                MapStatus(@event.Step),
                Array.Empty<string>(),
                now,
                now,
                @event.Step,
                @event.Message,
                @event.FilePath),
            (_, current) => current with
            {
                Status = MapStatus(@event.Step),
                UpdatedAt = now,
                LastStep = @event.Step,
                Message = @event.Message,
                OutputPath = @event.Step == RunnerStep.Completed ? @event.FilePath : current.OutputPath
            });
    }

    public void SetFailed(string correlationId, string message)
    {
        var now = DateTimeOffset.UtcNow;
        _runs.AddOrUpdate(
            correlationId,
            _ => new RunStatusInfo(
                correlationId,
                RunLifecycleStatus.Failed,
                Array.Empty<string>(),
                now,
                now,
                RunnerStep.Failed,
                message),
            (_, current) => current with
            {
                Status = RunLifecycleStatus.Failed,
                UpdatedAt = now,
                LastStep = RunnerStep.Failed,
                Message = message
            });
    }

    public RunStatusInfo? Get(string correlationId)
    {
        return _runs.TryGetValue(correlationId, out var run) ? run : null;
    }

    public IReadOnlyList<RunStatusInfo> List()
    {
        return _runs.Values
            .OrderByDescending(static x => x.UpdatedAt)
            .ToArray();
    }

    private static RunLifecycleStatus MapStatus(RunnerStep step)
    {
        return step switch
        {
            RunnerStep.Completed => RunLifecycleStatus.Completed,
            RunnerStep.Failed => RunLifecycleStatus.Failed,
            _ => RunLifecycleStatus.Running
        };
    }
}
