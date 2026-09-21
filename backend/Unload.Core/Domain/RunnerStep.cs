namespace Unload.Core;

/// <summary>
/// Перечисляет этапы жизненного цикла запуска выгрузки.
/// Используется в событиях и статусах для унифицированного отображения прогресса.
/// </summary>
public enum RunnerStep
{
    RequestAccepted,
    TargetsResolved,
    ScriptDiscovered,
    QueryStarted,
    QueryCompleted,
    ChunkCreated,
    FileWritten,
    ScriptCompleted,
    PublishedToGateway,
    Completed,
    Failed,
    /// <summary>
    /// Чанк передан writer-у для записи. Этап включает возможное ожидание внутренней блокировки файла.
    /// </summary>
    FileWriteStarted,
    /// <summary>
    /// Партия файлов была успешно передана в очередь gateway.
    /// </summary>
    GatewayBatchQueued
}
