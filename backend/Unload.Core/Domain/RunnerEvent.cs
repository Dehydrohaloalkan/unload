namespace Unload.Core;

/// <summary>
/// Событие выполнения выгрузки, публикуемое раннером в поток статусов.
/// Используется API/Console и in-memory store для отображения прогресса запуска.
/// </summary>
/// <param name="OccurredAt">Момент возникновения события в UTC.</param>
/// <param name="CorrelationId">Идентификатор запуска, к которому относится событие.</param>
/// <param name="Step">Шаг процесса, на котором сгенерировано событие.</param>
/// <param name="Message">Человекочитаемое описание события.</param>
/// <param name="TargetCode">Target-код (если событие относится к конкретной выборке).</param>
/// <param name="ScriptCode">Код скрипта (если событие относится к конкретному скрипту).</param>
/// <param name="Records">Количество обработанных записей (если применимо).</param>
/// <param name="FilePath">Путь к файлу результата (если применимо).</param>
public record RunnerEvent(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    RunnerStep Step,
    string Message,
    string? TargetCode = null,
    string? ScriptCode = null,
    int? Records = null,
    string? FilePath = null);
