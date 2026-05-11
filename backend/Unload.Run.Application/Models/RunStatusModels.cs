using Unload.Core;

namespace Unload.Run.Application;

/// <summary>
/// Перечисляет агрегированные состояния запуска на уровне application слоя.
/// Используется API и клиентами для отображения общего статуса процесса.
/// </summary>
public enum RunLifecycleStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
    CancellationRequested
}

/// <summary>
/// Перечисляет состояния выполнения конкретного мембера в рамках запуска.
/// </summary>
public enum MemberRunLifecycleStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Снимок состояния конкретного мембера в рамках запуска выгрузки.
/// </summary>
/// <param name="MemberName">Имя мембера.</param>
/// <param name="Status">Текущий статус мембера.</param>
/// <param name="LastStep">Последний обработанный шаг раннера для мембера.</param>
/// <param name="Message">Последнее сообщение о состоянии мембера.</param>
/// <param name="UpdatedAt">Время последнего обновления статуса мембера.</param>
public record MemberRunStatusInfo(
    string MemberName,
    MemberRunLifecycleStatus Status,
    RunnerStep? LastStep,
    string? Message,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Снимок состояния worker-потока в рамках активного запуска.
/// </summary>
/// <param name="WorkerId">Идентификатор worker-потока.</param>
/// <param name="State">Текстовое состояние worker-а.</param>
/// <param name="ScriptCode">Текущий скрипт, если worker занят.</param>
/// <param name="MemberName">Текущий мембер, если worker занят.</param>
/// <param name="UpdatedAt">Время последнего обновления состояния worker-а.</param>
public record RunWorkerStatusInfo(
    int WorkerId,
    string State,
    string? ScriptCode,
    string? MemberName,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Снимок выходного артефакта, созданного в рамках запуска.
/// </summary>
/// <param name="FileName">Имя файла артефакта.</param>
/// <param name="FilePath">Абсолютный путь к файлу на сервере.</param>
/// <param name="MemberName">Мембер, к которому относится файл, если применимо.</param>
/// <param name="ScriptCode">Скрипт, которым создан файл, если применимо.</param>
/// <param name="OccurredAt">Время появления артефакта.</param>
public record RunOutputArtifactInfo(
    string FileName,
    string FilePath,
    string? MemberName,
    string? ScriptCode,
    DateTimeOffset OccurredAt);

public record SenderFileDispatchStateInfo(
    string FilePath,
    DateTimeOffset SentAt);

public record SenderBatchStatusInfo(
    string BatchId,
    string MemberName,
    SenderBatchStatus Status,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<SenderFileDispatchStateInfo> SentFiles,
    string? Message = null);

/// <summary>
/// Снимок состояния конкретного запуска выгрузки.
/// Используется для REST-ответов и SignalR-событий <c>run_status</c>.
/// </summary>
/// <param name="CorrelationId">Идентификатор запуска.</param>
/// <param name="Status">Текущий агрегированный статус запуска.</param>
/// <param name="TargetCodes">Target-коды, запрошенные для выполнения.</param>
/// <param name="CreatedAt">Время создания записи статуса.</param>
/// <param name="UpdatedAt">Время последнего обновления статуса.</param>
/// <param name="LastStep">Последний шаг раннера, если уже получен.</param>
/// <param name="Message">Текстовое описание последнего состояния.</param>
/// <param name="OutputPath">Путь к результату, если запуск завершен успешно.</param>
/// <param name="MemberStatuses">Статусы мемберов, участвующих в запуске.</param>
/// <param name="OutputArtifacts">Список файлов, созданных в рамках запуска.</param>
/// <param name="WorkerStatuses">Текущие состояния worker-потоков.</param>
public record RunStatusInfo(
    string CorrelationId,
    string TaskCode,
    RunLifecycleStatus Status,
    IReadOnlyCollection<string> TargetCodes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RunnerStep? LastStep = null,
    string? Message = null,
    string? OutputPath = null,
    IReadOnlyDictionary<string, MemberRunStatusInfo>? MemberStatuses = null,
    IReadOnlyCollection<RunOutputArtifactInfo>? OutputArtifacts = null,
    IReadOnlyDictionary<int, RunWorkerStatusInfo>? WorkerStatuses = null,
    IReadOnlyDictionary<string, SenderBatchStatusInfo>? SenderBatches = null,
    bool PublishToMq = true);
