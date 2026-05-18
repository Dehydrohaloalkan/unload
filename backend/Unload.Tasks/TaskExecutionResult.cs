namespace Unload.Tasks;

/// <summary>Статус исполнения задачи.</summary>
public enum TaskExecutionStatus
{
    Accepted,
    Running,
    Completed,
    Failed,
    Cancelled,
    Blocked
}

/// <summary>
/// Единый результат запуска задачи выгрузки.
/// </summary>
/// <param name="TaskCode">Код задачи.</param>
/// <param name="ExecutionId">Идентификатор запуска (correlationId).</param>
/// <param name="Status">Статус исполнения.</param>
/// <param name="Message">Текстовый итог выполнения.</param>
/// <param name="ScriptsExecuted">Количество выполненных SQL-скриптов (для script-задач).</param>
/// <param name="FilesWritten">Количество сформированных файлов.</param>
/// <param name="OutputPath">Папка результатов, если была создана.</param>
public record TaskExecutionResult(
    string TaskCode,
    string ExecutionId,
    TaskExecutionStatus Status,
    string Message,
    int? ScriptsExecuted = null,
    int? FilesWritten = null,
    string? OutputPath = null);
