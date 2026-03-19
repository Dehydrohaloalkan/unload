namespace Unload.TaskFlow;

/// <summary>
/// Контракт in-memory состояния системных workflow-стадий.
/// </summary>
public interface IWorkflowStageStateStore
{
    /// <summary>
    /// Помечает стадию завершенной.
    /// </summary>
    void MarkCompleted(string stageCode);

    /// <summary>
    /// Возвращает признак завершения стадии.
    /// </summary>
    bool IsCompleted(string stageCode);

    /// <summary>
    /// Сбрасывает состояние всех системных стадий.
    /// </summary>
    void Reset();
}
