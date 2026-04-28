using Unload.Workflow;
using Unload.TaskFlow.Abstractions;

namespace Unload.TaskFlow;

public class TaskFlowRegistryInvariant(
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
