namespace Unload.Workflow;

/// <summary>
/// Контракт реестра задач workflow.
/// </summary>
public interface IWorkflowTaskRegistry
{
    /// <summary>
    /// Возвращает зарегистрированную задачу по коду или бросает исключение.
    /// </summary>
    IWorkflowTaskDefinition GetRequired(string taskCode);
}

