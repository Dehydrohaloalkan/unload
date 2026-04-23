using Unload.Workflow;

namespace Unload.TaskFlow;

/// <summary>
/// Описание пользовательской workflow-задачи в конфигурации pipeline.
/// </summary>
public  record TaskPipelineDescriptor(
    string TaskCode,
    Type DefinitionType,
    IReadOnlyCollection<string> RequiresCompletedCodes,
    IReadOnlyCollection<string> ConflictsWithTaskCodes,
    IReadOnlyCollection<Type> TransitionHandlerTypes)
{
    public WorkflowTaskRule ToRule()
    {
        return new WorkflowTaskRule(TaskCode, RequiresCompletedCodes, ConflictsWithTaskCodes);
    }
}
