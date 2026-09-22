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
        IReadOnlyCollection<string> memberOrScriptNames,
        bool publishToGateway,
        string taskCode,
        DateTimeOffset now)
    {
        var memberStatuses = memberOrScriptNames
            .Where(static memberName => !string.IsNullOrWhiteSpace(memberName))
            .Select(static memberName => memberName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((memberName, index) => new { memberName, QueuePosition = index + 1 })
            .ToDictionary(
                static item => item.memberName,
                item => new MemberRunStatusInfo(
                    item.memberName,
                    MemberRunLifecycleStatus.Pending,
                    LastStep: null,
                    Message: "Awaiting processing.",
                    UpdatedAt: now,
                    QueuePosition: item.QueuePosition),
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
            PublishToGateway: publishToGateway,
            ScriptStatuses: new Dictionary<string, ScriptRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
            FileStatuses: new Dictionary<string, FileRunStatusInfo>(StringComparer.OrdinalIgnoreCase));
    }

    public RunStatusInfo CreateFromEvent(RunnerEvent @event, DateTimeOffset now)
    {
        @event = RunnerFailureMessages.Sanitize(@event);
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
            SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase),
            ScriptStatuses: RunScriptProjector.Apply(
                new Dictionary<string, ScriptRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                @event),
            FileStatuses: RunFileProjector.Apply(
                new Dictionary<string, FileRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                @event),
            Failure: @event.Failure);
    }

    public RunStatusInfo ApplyRunnerEvent(RunStatusInfo current, RunnerEvent @event, DateTimeOffset now)
    {
        @event = RunnerFailureMessages.Sanitize(@event);
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
            Failure = @event.Failure ?? current.Failure,
            OutputPath = @event.Step == RunnerStep.Completed ? @event.FilePath : current.OutputPath,
            MemberStatuses = RunMemberProjector.Apply(current.MemberStatuses, @event, now),
            OutputArtifacts = RunArtifactProjector.Apply(current.OutputArtifacts, @event),
            WorkerStatuses = _workerProjector.Apply(current.WorkerStatuses, @event, now),
            SenderBatches = GatewayFeedbackProjector.ApplyQueued(current.SenderBatches, @event, now),
            ScriptStatuses = RunScriptProjector.Apply(current.ScriptStatuses, @event),
            FileStatuses = RunFileProjector.Apply(current.FileStatuses, @event)
        };

        return RunCompletionPolicy.Apply(updated, now);
    }

    public RunStatusInfo CreateFromSenderFeedback(SenderFileDispatchFeedback feedback, DateTimeOffset now)
    {
        var failure = SenderFailure(feedback);
        return new RunStatusInfo(
            CorrelationId: feedback.CorrelationId,
            TaskCode: RunTaskCodeResolver.Resolve(feedback.CorrelationId),
            Status: failure is null ? RunLifecycleStatus.Running : RunLifecycleStatus.Failed,
            PublishToGateway: true,
            TargetCodes: Array.Empty<string>(),
            CreatedAt: now,
            UpdatedAt: now,
            Message: "Sender feedback received.",
            MemberStatuses: failure is null || string.IsNullOrWhiteSpace(feedback.MemberName)
                ? new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)
                : RunMemberProjector.ApplyFailure(
                    new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                    feedback.MemberName,
                    failure,
                    now),
            OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
            WorkerStatuses: CreateInitialWorkerStatuses(now),
            SenderBatches: GatewayFeedbackProjector.Apply(
                source: null,
                feedback,
                now),
            ScriptStatuses: new Dictionary<string, ScriptRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
            FileStatuses: new Dictionary<string, FileRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
            Failure: failure);
    }

    public RunStatusInfo ApplySenderFeedback(RunStatusInfo current, SenderFileDispatchFeedback feedback, DateTimeOffset now)
    {
        var failure = SenderFailure(feedback);
        var updated = current with
        {
            UpdatedAt = now,
            Failure = failure ?? current.Failure,
            SenderBatches = GatewayFeedbackProjector.Apply(
                current.SenderBatches,
                feedback with { Failure = failure },
                now),
            MemberStatuses = failure is null || string.IsNullOrWhiteSpace(feedback.MemberName)
                ? current.MemberStatuses
                : RunMemberProjector.ApplyFailure(current.MemberStatuses, feedback.MemberName, failure, now)
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

    public RunStatusInfo UpdateToFailed(
        RunStatusInfo current,
        string message,
        DateTimeOffset now,
        RunnerFailureInfo? failure = null)
    {
        if (current.Status == RunLifecycleStatus.Failed && current.Failure is not null)
        {
            return current;
        }

        var failureEvent = new RunnerEvent(
            now,
            current.CorrelationId,
            RunnerStep.Failed,
            message,
            Failure: failure);
        return current with
        {
            Status = RunLifecycleStatus.Failed,
            UpdatedAt = now,
            LastStep = RunnerStep.Failed,
            Message = message,
            Failure = failure ?? current.Failure,
            MemberStatuses = RunMemberProjector.UpdateAll(
                current.MemberStatuses,
                MemberRunLifecycleStatus.Failed,
                RunnerStep.Failed,
                message,
                now,
                failure),
            WorkerStatuses = _workerProjector.Apply(current.WorkerStatuses, failureEvent, now),
            ScriptStatuses = RunScriptProjector.FailUnfinished(
                current.ScriptStatuses,
                message,
                now,
                failure),
            FileStatuses = RunFileProjector.FailUnfinished(current.FileStatuses, message, now, failure)
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
            WorkerStatuses = RunWorkerProjector.Reset(current.WorkerStatuses, now),
            ScriptStatuses = RunScriptProjector.CancelUnfinished(current.ScriptStatuses, message, now),
            FileStatuses = RunFileProjector.CancelUnfinished(current.FileStatuses, message, now)
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

    private static RunnerFailureInfo? SenderFailure(SenderFileDispatchFeedback feedback)
    {
        if (feedback.Failure is not null)
        {
            return RunnerFailureMessages.Sanitize(feedback.Failure);
        }

        if (feedback.Kind != SenderFeedbackKind.BatchFailed)
        {
            return null;
        }

        return new RunnerFailureInfo(
                "sender",
                "batch",
                feedback.BatchId,
                feedback.MemberName,
                null,
                null,
                null,
                feedback.FilePath,
                feedback.BatchId,
                "SENDER_BATCH_FAILED",
                RunnerFailureMessages.ForStage("sender"),
                feedback.OccurredAt);
    }
}
