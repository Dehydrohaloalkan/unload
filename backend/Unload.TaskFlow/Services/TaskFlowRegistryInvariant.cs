using Unload.Workflow;

namespace Unload.TaskFlow;

public interface ITaskFlowRegistryInvariant
{
    void EnsureValid();
}

public  class TaskFlowRegistryInvariant : ITaskFlowRegistryInvariant
{
    private readonly TaskPipeline _pipeline;
    private readonly IWorkflowTaskRegistry _registry;

    public TaskFlowRegistryInvariant(
        TaskPipeline pipeline,
        IWorkflowTaskRegistry registry)
    {
        _pipeline = pipeline;
        _registry = registry;
    }

    public void EnsureValid()
    {
        foreach (var taskCode in _pipeline.Tasks.Select(static x => x.TaskCode))
        {
            _ = _registry.GetRequired(taskCode);
        }
    }
}
