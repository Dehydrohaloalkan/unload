namespace Unload.Application;

/// <summary>
/// Контракт каталога зависимостей между пользовательскими workflow-задачами.
/// </summary>
public interface IWorkflowTaskDependencyCatalog
{
    /// <summary>
    /// Возвращает правило задачи по коду.
    /// </summary>
    WorkflowTaskRule GetRequired(string taskCode);
}
