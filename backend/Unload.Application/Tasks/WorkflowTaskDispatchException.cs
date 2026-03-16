namespace Unload.Application;

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
public sealed class WorkflowTaskDispatchException : Exception
{
    public WorkflowTaskDispatchException(
        WorkflowTaskFailureKind failureKind,
        string errorCode,
        string message,
        IReadOnlyDictionary<string, object?>? extensions = null)
        : base(message)
    {
        FailureKind = failureKind;
        ErrorCode = errorCode;
        Extensions = extensions;
    }

    public WorkflowTaskFailureKind FailureKind { get; }

    public string ErrorCode { get; }

    public IReadOnlyDictionary<string, object?>? Extensions { get; }
}
