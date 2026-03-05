using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Перечисляет агрегированные состояния запуска на уровне application слоя.
/// Используется API и клиентами для отображения общего статуса процесса.
/// </summary>
public enum RunLifecycleStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
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
public record RunStatusInfo(
    string CorrelationId,
    RunLifecycleStatus Status,
    IReadOnlyCollection<string> TargetCodes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RunnerStep? LastStep = null,
    string? Message = null,
    string? OutputPath = null,
    IReadOnlyDictionary<string, MemberRunStatusInfo>? MemberStatuses = null);
