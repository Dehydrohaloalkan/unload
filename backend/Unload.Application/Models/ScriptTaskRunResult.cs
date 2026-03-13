namespace Unload.Application;

/// <summary>
/// Результат выполнения дополнительной задачи выгрузки.
/// </summary>
/// <param name="TaskName">Код задачи.</param>
/// <param name="CorrelationId">Идентификатор запуска задачи.</param>
/// <param name="ScriptsExecuted">Количество выполненных SQL-скриптов.</param>
/// <param name="FilesWritten">Количество сформированных файлов.</param>
/// <param name="OutputPath">Папка результатов, если она была создана.</param>
/// <param name="Message">Текстовый итог выполнения.</param>
public record ScriptTaskRunResult(
    string TaskName,
    string CorrelationId,
    int ScriptsExecuted,
    int FilesWritten,
    string? OutputPath,
    string Message);
