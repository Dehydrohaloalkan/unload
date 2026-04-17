namespace Unload.TaskFlow;

/// <summary>
/// Каталог зависимостей задач, собранный из централизованной конфигурации pipeline.
/// </summary>
public sealed class WorkflowTaskDependencyCatalog : IWorkflowTaskDependencyCatalog
{
    private readonly IReadOnlyDictionary<string, WorkflowTaskRule> _rules;

    public WorkflowTaskDependencyCatalog(TaskPipeline pipeline)
    {
        _rules = pipeline.Tasks.ToDictionary(
            static x => x.TaskCode,
            static x => x.ToRule(),
            StringComparer.OrdinalIgnoreCase);
    }

    public WorkflowTaskRule GetRequired(string taskCode)
    {
        if (_rules.TryGetValue(taskCode, out var rule))
        {
            return rule;
        }

        throw new InvalidOperationException($"Workflow task dependency rule '{taskCode}' is not configured.");
    }
}
