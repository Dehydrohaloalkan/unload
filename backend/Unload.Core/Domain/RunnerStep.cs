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
    PublishedToMq,
    Completed,
    Failed
}
