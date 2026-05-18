namespace Unload.Tasks;

/// <summary>Категория отказа запуска задачи.</summary>
public enum TaskLaunchFailureKind
{
    Validation,
    Conflict
}

/// <summary>
/// Бизнес-ошибка запуска задачи выгрузки.
/// Заменяет <c>WorkflowTaskDispatchException</c>.
/// </summary>
public class TaskLaunchException(
    TaskLaunchFailureKind failureKind,
    string errorCode,
    string message,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(message)
{
    /// <summary>Категория отказа.</summary>
    public TaskLaunchFailureKind FailureKind { get; } = failureKind;

    /// <summary>Машинный код ошибки.</summary>
    public string ErrorCode { get; } = errorCode;

    /// <summary>Дополнительные поля для diagnostics-контракта.</summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; } = extensions;
}
