namespace Unload.Core;

/// <summary>
/// Перечисляет этапы жизненного цикла запуска выгрузки.
/// Используется в событиях и статусах для унифицированного отображения прогресса.
/// </summary>
public enum RunnerStep
{
    QueryStarted = 3,
    FileWritten = 6,
    ScriptCompleted = 7,
    Completed = 9,
    Failed = 10,
    /// <summary>
    /// Партия файлов была успешно передана в очередь gateway.
    /// </summary>
    GatewayBatchQueued = 12
}
