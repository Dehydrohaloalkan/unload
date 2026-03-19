namespace Unload.TaskFlow;

/// <summary>
/// Контекст завершения workflow-задачи для автоматических переходов.
/// </summary>
/// <param name="TaskCode">Код завершившейся задачи.</param>
/// <param name="Payload">Дополнительный payload результата завершения.</param>
public sealed record WorkflowTaskCompletionContext(
    string TaskCode,
    object? Payload);
