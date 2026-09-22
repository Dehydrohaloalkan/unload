namespace Unload.Core;

/// <summary>
/// Событие выполнения выгрузки, публикуемое раннером в поток статусов.
/// Используется API/Console и in-memory store для отображения прогресса запуска.
/// </summary>
/// <param name="OccurredAt">Момент возникновения события в UTC.</param>
/// <param name="CorrelationId">Идентификатор запуска, к которому относится событие.</param>
/// <param name="Step">Шаг процесса, на котором сгенерировано событие.</param>
/// <param name="Message">Человекочитаемое описание события.</param>
/// <param name="MemberName">Имя мембера (если событие относится к конкретному мемберу).</param>
/// <param name="ScriptCode">Код скрипта (если событие относится к конкретному скрипту).</param>
/// <param name="Records">Количество обработанных записей (если применимо).</param>
/// <param name="FilePath">Путь к файлу результата (если применимо).</param>
/// <param name="WorkerId">Идентификатор worker-потока, если событие относится к конкретному worker.</param>
/// <param name="ChunkNumber">Номер чанка в рамках мембера (если событие относится к файлу).</param>
/// <param name="EstimatedBytes">Оценочный размер чанка до записи либо размер записанного чанка (если применимо).</param>
/// <param name="BatchId">Идентификатор gateway-партии, если событие относится к постановке партии в очередь.</param>
/// <param name="BatchFileCount">Число файлов в gateway-партии, если применимо.</param>
/// <param name="Sequence">Монотонный порядковый номер события внутри запуска.</param>
/// <param name="WorkOrder">Стабильный порядковый номер скрипта в discovery/queue order.</param>
/// <param name="Failure">Структурированная диагностика ошибки, если событие сообщает об ошибке.</param>
public record RunnerEvent(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    RunnerStep Step,
    string Message,
    string? MemberName = null,
    string? ScriptCode = null,
    int? Records = null,
    string? FilePath = null,
    int? WorkerId = null,
    int? ChunkNumber = null,
    long? EstimatedBytes = null,
    string? BatchId = null,
    int? BatchFileCount = null,
    long Sequence = 0,
    int? WorkOrder = null,
    RunnerFailureInfo? Failure = null);
