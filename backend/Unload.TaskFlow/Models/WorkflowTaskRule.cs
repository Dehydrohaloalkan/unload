namespace Unload.TaskFlow;

/// <summary>
/// Правило зависимостей workflow-задачи.
/// </summary>
/// <param name="TaskCode">Код задачи.</param>
/// <param name="RequiresCompletedCodes">Список task/stage кодов, которые должны быть успешно завершены заранее.</param>
/// <param name="ConflictsWithTaskCodes">Список задач, с которыми одновременное выполнение запрещено.</param>
public  record WorkflowTaskRule(
    string TaskCode,
    IReadOnlyCollection<string> RequiresCompletedCodes,
    IReadOnlyCollection<string> ConflictsWithTaskCodes);
