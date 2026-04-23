using Unload.Workflow;

namespace Unload.TaskFlow;

public interface ITaskFlowRegistryInvariant
{
    void EnsureValid();
}

public  class TaskFlowRegistryInvariant(
    TaskPipeline pipeline,
    IWorkflowTaskRegistry registry) : ITaskFlowRegistryInvariant
{
    private readonly TaskPipeline _pipeline = pipeline;
    private readonly IWorkflowTaskRegistry _registry = registry;

    public void EnsureValid()
    {
        foreach (var taskCode in _pipeline.Tasks.Select(static x => x.TaskCode))
        {
            _ = _registry.GetRequired(taskCode);
        }
    }
}
