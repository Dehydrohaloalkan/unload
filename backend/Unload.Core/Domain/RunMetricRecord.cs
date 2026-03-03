namespace Unload.Core;

/// <summary>
/// Метрика длительности этапа запуска выгрузки.
/// Используется диагностическим sink для записи в <c>metrics.csv</c> и анализа производительности.
/// </summary>
/// <param name="OccurredAt">Момент фиксации метрики в UTC.</param>
/// <param name="CorrelationId">Идентификатор запуска.</param>
/// <param name="Step">Этап выполнения, для которого измерена длительность.</param>
/// <param name="DurationMs">Длительность этапа в миллисекундах.</param>
/// <param name="Outcome">Результат этапа: success/error/cancelled и т.д.</param>
/// <param name="ProfileCode">Код профиля (если этап профильно-специфичен).</param>
/// <param name="ScriptCode">Код скрипта (если этап скриптовый).</param>
/// <param name="Records">Количество обработанных строк (если применимо).</param>
/// <param name="FilePath">Путь к файлу результата (если применимо).</param>
/// <param name="Details">Дополнительные детали для диагностики.</param>
public record RunMetricRecord(
    DateTimeOffset OccurredAt,
    string CorrelationId,
    RunnerStep Step,
    long DurationMs,
    string Outcome,
    string? ProfileCode = null,
    string? ScriptCode = null,
    int? Records = null,
    string? FilePath = null,
    string? Details = null);
