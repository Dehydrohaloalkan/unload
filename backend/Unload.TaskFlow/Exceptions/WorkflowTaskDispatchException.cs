namespace Unload.TaskFlow.Exceptions;

/// <summary>
/// Категория отказа диспетчеризации workflow-задачи.
/// </summary>
public enum WorkflowTaskFailureKind
{
    Validation,
    Conflict
}

/// <summary>
/// Бизнес-ошибка dispatch-слоя workflow.
/// </summary>
public class WorkflowTaskDispatchException(
    WorkflowTaskFailureKind failureKind,
    string errorCode,
    string message,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(message)
{
    public WorkflowTaskFailureKind FailureKind { get; } = failureKind;

    public string ErrorCode { get; } = errorCode;

    public IReadOnlyDictionary<string, object?>? Extensions { get; } = extensions;
}
