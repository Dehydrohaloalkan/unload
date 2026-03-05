namespace Unload.WebConsole;

internal record RunStartRequest(IReadOnlyCollection<string> TargetCodes);

internal record RunAcceptedResponse(
    string CorrelationId,
    string RunStatusPath,
    string HubPath,
    string SubscribeMethod,
    string EventName,
    string RunStatusEventName);

internal record RunConflictResponse(string Message, string? ActiveCorrelationId);

internal record RunStartResult(RunAcceptedResponse? Accepted, RunConflictResponse? Conflict);

internal enum RunLifecycleStatus
{
    Running,
    Completed,
    Failed
}

internal enum RunnerStep
{
    RequestAccepted,
    TargetsResolved,
    ScriptDiscovered,
    QueryStarted,
    QueryCompleted,
    ChunkCreated,
    FileWritten,
    ScriptCompleted,
    PublishedToMq,
    Completed,
    Failed
}

internal record RunStatusInfoDto(
    string CorrelationId,
    RunLifecycleStatus Status,
    IReadOnlyCollection<string> TargetCodes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RunnerStep? LastStep,
    string? Message,
    string? OutputPath);

internal record RunnerEventDto(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    RunnerStep Step,
    string Message,
    string? TargetCode,
    string? ScriptCode,
    int? Records,
    string? FilePath);

internal record RunnerEventLine(DateTime Time, RunnerStep Step, string Message);

internal record UiSnapshot(
    RunLifecycleStatus Status,
    RunnerStep? LastStep,
    string? Message,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<RunnerEventLine> Events);
