namespace Unload.TaskFlow;

/// <summary>
/// Итоговая конфигурация task pipeline.
/// </summary>
public sealed class TaskPipeline
{
    private readonly IReadOnlyDictionary<string, TaskPipelineDescriptor> _descriptorsByCode;

    public TaskPipeline(IReadOnlyCollection<TaskPipelineDescriptor> descriptors)
    {
        Tasks = descriptors.ToArray();
        _descriptorsByCode = Tasks.ToDictionary(
            static x => x.TaskCode,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Задачи pipeline в порядке регистрации.
    /// </summary>
    public IReadOnlyList<TaskPipelineDescriptor> Tasks { get; }

    /// <summary>
    /// Возвращает описание задачи по коду.
    /// </summary>
    public TaskPipelineDescriptor GetRequired(string taskCode)
    {
        if (_descriptorsByCode.TryGetValue(taskCode, out var descriptor))
        {
            return descriptor;
        }

        throw new InvalidOperationException($"Task '{taskCode}' is not configured in the pipeline.");
    }
}
