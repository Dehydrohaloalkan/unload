using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Перечисляет агрегированные состояния запуска на уровне application слоя.
/// Используется API и клиентами для отображения общего статуса процесса.
/// </summary>
public enum RunLifecycleStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

/// <summary>
/// Снимок состояния конкретного запуска выгрузки.
/// Используется для REST-ответов и SignalR-событий <c>run_status</c>.
/// </summary>
/// <param name="CorrelationId">Идентификатор запуска.</param>
/// <param name="Status">Текущий агрегированный статус запуска.</param>
/// <param name="ProfileCodes">Коды профилей, запрошенных для выполнения.</param>
/// <param name="CreatedAt">Время создания записи статуса.</param>
/// <param name="UpdatedAt">Время последнего обновления статуса.</param>
/// <param name="LastStep">Последний шаг раннера, если уже получен.</param>
/// <param name="Message">Текстовое описание последнего состояния.</param>
/// <param name="OutputPath">Путь к результату, если запуск завершен успешно.</param>
public record RunStatusInfo(
    string CorrelationId,
    RunLifecycleStatus Status,
    IReadOnlyCollection<string> ProfileCodes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RunnerStep? LastStep = null,
    string? Message = null,
    string? OutputPath = null);
