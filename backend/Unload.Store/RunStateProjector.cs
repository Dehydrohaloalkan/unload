using System.Text.RegularExpressions;
using Unload.Core;

namespace Unload.Store;

internal sealed class RunStateProjector
{
    private const string TaskCodeRun = "run";
    private static readonly Regex WorkerIdRegex = new(
        @"Worker\s*#(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly int _workerCount;

    public RunStateProjector(int workerCount)
    {
        _workerCount = Math.Max(1, workerCount);
    }

    public IReadOnlyDictionary<int, RunWorkerStatusInfo> CreateInitialWorkerStatuses(DateTimeOffset now)
    {
        return Enumerable.Range(1, _workerCount)
            .ToDictionary(
                static workerId => workerId,
                workerId => new RunWorkerStatusInfo(
                    workerId,
                    "idle",
                    null,
                    null,
                    now));
    }

    public RunStatusInfo CreateFromEvent(RunnerEvent @event, DateTimeOffset now)
    {
        return new RunStatusInfo(
            CorrelationId: @event.CorrelationId,
            TaskCode: TaskCodeRun,
            Status: MapStatus(@event.Step),
            PublishToGateway: true,
            TargetCodes: Array.Empty<string>(),
            CreatedAt: now,
            UpdatedAt: now,
            LastStep: @event.Step,
            Message: @event.Message,
            OutputPath: @event.FilePath,
            MemberStatuses: ApplyMemberEvent(
                new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                @event,
                now),
            OutputArtifacts: ApplyArtifacts(Array.Empty<RunOutputArtifactInfo>(), @event),
            WorkerStatuses: ApplyWorkerEvent(CreateInitialWorkerStatuses(now), @event, now),
            SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase));
    }

    public RunStatusInfo ApplyRunnerEvent(RunStatusInfo current, RunnerEvent @event, DateTimeOffset now)
    {
        if (IsTerminalStatus(current.Status))
        {
            return current;
        }

        if (current.Status == RunLifecycleStatus.CancellationRequested &&
            @event.Step is not RunnerStep.Completed and not RunnerStep.Failed)
        {
            return current;
        }

        var updated = current with
        {
            Status = MapStatus(@event.Step),
            UpdatedAt = now,
            LastStep = @event.Step,
            Message = @event.Message,
            OutputPath = @event.Step == RunnerStep.Completed ? @event.FilePath : current.OutputPath,
            MemberStatuses = ApplyMemberEvent(current.MemberStatuses, @event, now),
            OutputArtifacts = ApplyArtifacts(current.OutputArtifacts, @event),
            WorkerStatuses = ApplyWorkerEvent(current.WorkerStatuses, @event, now)
        };

        return RunCompletionPolicy.Apply(updated, now);
    }

    public RunStatusInfo CreateFromSenderFeedback(SenderFileDispatchFeedback feedback, DateTimeOffset now)
    {
        return new RunStatusInfo(
            CorrelationId: feedback.CorrelationId,
            TaskCode: RunStateStore.ResolveTaskCodeByCorrelationId(feedback.CorrelationId),
            Status: RunLifecycleStatus.Running,
            PublishToGateway: true,
            TargetCodes: Array.Empty<string>(),
            CreatedAt: now,
            UpdatedAt: now,
            Message: "Sender feedback received.",
            MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
            OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
            WorkerStatuses: CreateInitialWorkerStatuses(now),
            SenderBatches: GatewayFeedbackProjector.Apply(
                source: null,
                feedback,
                now));
    }

    public RunStatusInfo ApplySenderFeedback(RunStatusInfo current, SenderFileDispatchFeedback feedback, DateTimeOffset now)
    {
        var updated = current with
        {
            UpdatedAt = now,
            SenderBatches = GatewayFeedbackProjector.Apply(current.SenderBatches, feedback, now)
        };
        return RunCompletionPolicy.Apply(updated, now);
    }

    public RunStatusInfo UpdateForRunning(RunStatusInfo current, DateTimeOffset now)
    {
        if (IsTerminalStatus(current.Status))
        {
            return current;
        }

        return current with
        {
            Status = RunLifecycleStatus.Running,
            UpdatedAt = now,
            Message = "Run started.",
            WorkerStatuses = current.WorkerStatuses is null || current.WorkerStatuses.Count == 0
                ? CreateInitialWorkerStatuses(now)
                : current.WorkerStatuses
        };
    }

    public RunStatusInfo UpdateToFailed(RunStatusInfo current, string message, DateTimeOffset now)
    {
        return current with
        {
            Status = RunLifecycleStatus.Failed,
            UpdatedAt = now,
            LastStep = RunnerStep.Failed,
            Message = message,
            MemberStatuses = UpdateAllMemberStatuses(
                current.MemberStatuses,
                MemberRunLifecycleStatus.Failed,
                RunnerStep.Failed,
                message,
                now),
            WorkerStatuses = ResetWorkers(current.WorkerStatuses, now)
        };
    }

    public RunStatusInfo UpdateToCancellationRequested(RunStatusInfo current, string message, DateTimeOffset now)
    {
        if (IsTerminalStatus(current.Status))
        {
            return current;
        }

        return current with
        {
            Status = RunLifecycleStatus.CancellationRequested,
            UpdatedAt = now,
            Message = message,
            WorkerStatuses = current.WorkerStatuses is null || current.WorkerStatuses.Count == 0
                ? CreateInitialWorkerStatuses(now)
                : current.WorkerStatuses
        };
    }

    public RunStatusInfo UpdateToCancelled(RunStatusInfo current, string message, DateTimeOffset now)
    {
        if (IsTerminalStatus(current.Status))
        {
            return current;
        }

        return current with
        {
            Status = RunLifecycleStatus.Cancelled,
            UpdatedAt = now,
            LastStep = RunnerStep.Failed,
            Message = message,
            MemberStatuses = UpdateAllMemberStatuses(
                current.MemberStatuses,
                MemberRunLifecycleStatus.Cancelled,
                RunnerStep.Failed,
                message,
                now),
            WorkerStatuses = ResetWorkers(current.WorkerStatuses, now)
        };
    }

    private static RunLifecycleStatus MapStatus(RunnerStep step)
    {
        return step switch
        {
            RunnerStep.Completed => RunLifecycleStatus.Running,
            RunnerStep.Failed => RunLifecycleStatus.Failed,
            _ => RunLifecycleStatus.Running
        };
    }

    private static bool IsTerminalStatus(RunLifecycleStatus status)
    {
        return status is RunLifecycleStatus.Completed or RunLifecycleStatus.Failed or RunLifecycleStatus.Cancelled;
    }

    private static IReadOnlyDictionary<string, MemberRunStatusInfo> ApplyMemberEvent(
        IReadOnlyDictionary<string, MemberRunStatusInfo>? source,
        RunnerEvent @event,
        DateTimeOffset now)
    {
        var map = source is null
            ? new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, MemberRunStatusInfo>(source, StringComparer.OrdinalIgnoreCase);

        if (@event.Step == RunnerStep.Completed)
        {
            return UpdateAllMemberStatuses(map, MemberRunLifecycleStatus.Completed, @event.Step, @event.Message, now);
        }

        if (@event.Step == RunnerStep.Failed && string.IsNullOrWhiteSpace(@event.MemberName))
        {
            return UpdateAllMemberStatuses(map, MemberRunLifecycleStatus.Failed, @event.Step, @event.Message, now);
        }

        if (string.IsNullOrWhiteSpace(@event.MemberName))
        {
            return map;
        }

        var memberName = @event.MemberName.Trim();
        var status = @event.Step switch
        {
            RunnerStep.Failed => MemberRunLifecycleStatus.Failed,
            RunnerStep.ScriptCompleted => MemberRunLifecycleStatus.Completed,
            _ => MemberRunLifecycleStatus.Running
        };
        map[memberName] = new MemberRunStatusInfo(
            memberName,
            status,
            @event.Step,
            @event.Message,
            now);

        return map;
    }

    private static IReadOnlyDictionary<string, MemberRunStatusInfo> UpdateAllMemberStatuses(
        IReadOnlyDictionary<string, MemberRunStatusInfo>? source,
        MemberRunLifecycleStatus status,
        RunnerStep step,
        string? message,
        DateTimeOffset now)
    {
        if (source is null || source.Count == 0)
        {
            return new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase);
        }

        return source.ToDictionary(
            static x => x.Key,
            x => x.Value with
            {
                Status = status,
                LastStep = step,
                Message = message,
                UpdatedAt = now
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<RunOutputArtifactInfo> ApplyArtifacts(
        IReadOnlyCollection<RunOutputArtifactInfo>? source,
        RunnerEvent @event)
    {
        var artifacts = source?.ToList() ?? [];
        if (@event.Step != RunnerStep.FileWritten || string.IsNullOrWhiteSpace(@event.FilePath))
        {
            return artifacts;
        }

        if (artifacts.Any(x => string.Equals(x.FilePath, @event.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            return artifacts;
        }

        artifacts.Add(new RunOutputArtifactInfo(
            Path.GetFileName(@event.FilePath),
            @event.FilePath,
            @event.MemberName,
            @event.ScriptCode,
            @event.OccurredAt));
        return artifacts
            .OrderByDescending(static x => x.OccurredAt)
            .ToArray();
    }

    private static IReadOnlyDictionary<int, RunWorkerStatusInfo> ApplyWorkerEvent(
        IReadOnlyDictionary<int, RunWorkerStatusInfo>? source,
        RunnerEvent @event,
        DateTimeOffset now)
    {
        var map = source is null
            ? new Dictionary<int, RunWorkerStatusInfo>()
            : new Dictionary<int, RunWorkerStatusInfo>(source);

        if (@event.Step is RunnerStep.Completed or RunnerStep.Failed)
        {
            return ResetWorkers(map, now);
        }

        var workerId = @event.WorkerId ?? TryExtractWorkerId(@event.Message);
        if (workerId is null)
        {
            return map;
        }

        if (@event.Step == RunnerStep.QueryStarted)
        {
            map[workerId.Value] = new RunWorkerStatusInfo(
                workerId.Value,
                "running",
                @event.ScriptCode,
                @event.MemberName,
                now);
            return map;
        }

        if (@event.Step == RunnerStep.QueryCompleted)
        {
            map[workerId.Value] = new RunWorkerStatusInfo(
                workerId.Value,
                "idle",
                null,
                null,
                now);
        }

        return map;
    }

    public static IReadOnlyDictionary<int, RunWorkerStatusInfo> ResetWorkers(
        IReadOnlyDictionary<int, RunWorkerStatusInfo>? source,
        DateTimeOffset now)
    {
        if (source is null || source.Count == 0)
        {
            return new Dictionary<int, RunWorkerStatusInfo>();
        }

        return source.ToDictionary(
            static x => x.Key,
            x => x.Value with
            {
                State = "idle",
                ScriptCode = null,
                MemberName = null,
                UpdatedAt = now
            });
    }

    private static int? TryExtractWorkerId(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var match = WorkerIdRegex.Match(message);
        return match.Success && int.TryParse(match.Groups["id"].Value, out var workerId)
            ? workerId
            : null;
    }
}
