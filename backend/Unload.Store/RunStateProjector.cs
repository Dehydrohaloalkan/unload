using Unload.Core;

namespace Unload.Store;

internal sealed class RunStateProjector
{
    private const string TaskCodeRun = "run";
    private readonly RunWorkerProjector _workerProjector;

    public RunStateProjector(int workerCount)
    {
        _workerProjector = new RunWorkerProjector(workerCount);
    }

    public IReadOnlyDictionary<int, RunWorkerStatusInfo> CreateInitialWorkerStatuses(DateTimeOffset now)
    {
        return _workerProjector.CreateInitial(now);
    }

    public RunStatusInfo CreateStarted(
        string correlationId,
        IReadOnlyCollection<string> targetCodes,
        IReadOnlyCollection<string> memberNames,
        bool publishToGateway,
        string taskCode,
        DateTimeOffset now)
    {
        var memberStatuses = memberNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static memberName => memberName,
                memberName => new MemberRunStatusInfo(
                    memberName,
                    MemberRunLifecycleStatus.Pending,
                    LastStep: null,
                    Message: "Awaiting processing.",
                    UpdatedAt: now),
                StringComparer.OrdinalIgnoreCase);

        return new RunStatusInfo(
            correlationId,
            taskCode,
            RunLifecycleStatus.Running,
            targetCodes.ToArray(),
            now,
            now,
            Message: "Run started.",
            MemberStatuses: memberStatuses,
            OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
            WorkerStatuses: CreateInitialWorkerStatuses(now),
            SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase),
            PublishToGateway: publishToGateway);
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
            MemberStatuses: RunMemberProjector.Apply(
                new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                @event,
                now),
            OutputArtifacts: RunArtifactProjector.Apply(Array.Empty<RunOutputArtifactInfo>(), @event),
            WorkerStatuses: _workerProjector.Apply(CreateInitialWorkerStatuses(now), @event, now),
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
            MemberStatuses = RunMemberProjector.Apply(current.MemberStatuses, @event, now),
            OutputArtifacts = RunArtifactProjector.Apply(current.OutputArtifacts, @event),
            WorkerStatuses = _workerProjector.Apply(current.WorkerStatuses, @event, now)
        };

        return RunCompletionPolicy.Apply(updated, now);
    }

    public RunStatusInfo CreateFromSenderFeedback(SenderFileDispatchFeedback feedback, DateTimeOffset now)
    {
        return new RunStatusInfo(
            CorrelationId: feedback.CorrelationId,
            TaskCode: RunTaskCodeResolver.Resolve(feedback.CorrelationId),
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
            MemberStatuses = RunMemberProjector.UpdateAll(
                current.MemberStatuses,
                MemberRunLifecycleStatus.Failed,
                RunnerStep.Failed,
                message,
                now),
            WorkerStatuses = RunWorkerProjector.Reset(current.WorkerStatuses, now)
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
            MemberStatuses = RunMemberProjector.UpdateAll(
                current.MemberStatuses,
                MemberRunLifecycleStatus.Cancelled,
                RunnerStep.Failed,
                message,
                now),
            WorkerStatuses = RunWorkerProjector.Reset(current.WorkerStatuses, now)
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
}
