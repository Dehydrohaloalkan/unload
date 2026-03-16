namespace Unload.Application;

/// <summary>
/// Централизованный каталог зависимостей между пользовательскими workflow-задачами.
/// Новые последовательности before/after добавляются здесь.
/// </summary>
public sealed class WorkflowTaskDependencyCatalog : IWorkflowTaskDependencyCatalog
{
    private readonly IReadOnlyDictionary<string, WorkflowTaskRule> _rules = new Dictionary<string, WorkflowTaskRule>(
        StringComparer.OrdinalIgnoreCase)
    {
        [WorkflowTaskCodes.Preset] = new(
            WorkflowTaskCodes.Preset,
            new[] { WorkflowStageCodes.PresetProbeReady },
            new[] { WorkflowTaskCodes.Run, WorkflowTaskCodes.Extra }),
        [WorkflowTaskCodes.Run] = new(
            WorkflowTaskCodes.Run,
            new[] { WorkflowTaskCodes.Preset },
            new[] { WorkflowTaskCodes.Preset }),
        [WorkflowTaskCodes.Extra] = new(
            WorkflowTaskCodes.Extra,
            new[] { WorkflowTaskCodes.Preset },
            new[] { WorkflowTaskCodes.Preset })
    };

    public WorkflowTaskRule GetRequired(string taskCode)
    {
        if (_rules.TryGetValue(taskCode, out var rule))
        {
            return rule;
        }

        return new WorkflowTaskRule(taskCode, Array.Empty<string>(), Array.Empty<string>());
    }
}
