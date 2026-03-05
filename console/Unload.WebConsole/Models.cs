namespace Unload.WebConsole;

/// <summary>
/// Тело запроса запуска выгрузки через API.
/// </summary>
internal record RunStartRequest(IReadOnlyCollection<string> MemberCodes);

/// <summary>
/// Успешный ответ API при создании нового запуска.
/// </summary>
internal record RunAcceptedResponse(
    string CorrelationId,
    string RunStatusPath,
    string HubPath,
    string SubscribeMethod,
    string EventName,
    string RunStatusEventName,
    string StopPath);

/// <summary>
/// Ответ API при конфликте запуска, когда уже есть активная выгрузка.
/// </summary>
internal record RunConflictResponse(string Message, string? ActiveCorrelationId);

/// <summary>
/// Результат попытки запуска: либо accepted, либо conflict.
/// </summary>
internal record RunStartResult(RunAcceptedResponse? Accepted, RunConflictResponse? Conflict);

/// <summary>
/// Агрегированное состояние выполнения выгрузки.
/// </summary>
internal enum RunLifecycleStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Статус выполнения конкретного мембера.
/// </summary>
internal enum MemberRunLifecycleStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Шаги пайплайна раннера, приходящие в статусных событиях.
/// </summary>
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

/// <summary>
/// DTO статуса запуска, получаемый из API/SignalR.
/// </summary>
internal record RunStatusInfoDto(
    string CorrelationId,
    RunLifecycleStatus Status,
    IReadOnlyCollection<string> TargetCodes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RunnerStep? LastStep,
    string? Message,
    string? OutputPath,
    IReadOnlyDictionary<string, MemberRunStatusInfoDto>? MemberStatuses);

/// <summary>
/// Статус конкретного мембера для отображения в UI.
/// </summary>
internal record MemberRunStatusInfoDto(
    string MemberName,
    MemberRunLifecycleStatus Status,
    RunnerStep? LastStep,
    string? Message,
    DateTimeOffset UpdatedAt);

/// <summary>
/// DTO события раннера, транслируемого через SignalR.
/// </summary>
internal record RunnerEventDto(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    RunnerStep Step,
    string Message,
    string? TargetCode,
    string? ScriptCode,
    int? Records,
    string? FilePath);

/// <summary>
/// Строка события для отображения в live-таблице.
/// </summary>
internal record RunnerEventLine(DateTime Time, RunnerStep Step, string Message);

/// <summary>
/// Снимок UI-состояния для построения панели и ленты событий.
/// </summary>
internal record UiSnapshot(
    RunLifecycleStatus Status,
    RunnerStep? LastStep,
    string? Message,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<RunnerEventLine> Events,
    IReadOnlyList<MemberRunStatusInfoDto> Members);
