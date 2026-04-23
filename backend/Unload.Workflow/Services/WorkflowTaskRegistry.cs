namespace Unload.Workflow;

/// <summary>
/// In-memory реестр зарегистрированных задач workflow.
/// </summary>
public  class WorkflowTaskRegistry : IWorkflowTaskRegistry
{
    private readonly IReadOnlyDictionary<string, IWorkflowTaskDefinition> _definitions;

    public WorkflowTaskRegistry(IEnumerable<IWorkflowTaskDefinition> definitions)
    {
        _definitions = definitions.ToDictionary(
            static x => x.TaskCode,
            StringComparer.OrdinalIgnoreCase);
    }

    public IWorkflowTaskDefinition GetRequired(string taskCode)
    {
        if (_definitions.TryGetValue(taskCode, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Workflow task '{taskCode}' is not registered.");
    }
}
