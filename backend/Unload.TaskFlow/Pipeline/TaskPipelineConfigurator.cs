namespace Unload.TaskFlow;

/// <summary>
/// Централизованная конфигурация порядка, зависимостей и post-actions task pipeline.
/// </summary>
public static class TaskPipelineConfigurator
{
    /// <summary>
    /// Описывает дефолтный pipeline пользовательских задач.
    /// </summary>
    public static TaskPipeline BuildDefault()
    {
        var builder = new TaskPipelineBuilder();

        builder
            .AddTask<RunPresetWorkflowTaskDefinition>(WorkflowTaskCodes.Preset)
            .RequiresCompleted(WorkflowStageCodes.PresetProbeReady)
            .ConflictsWith(WorkflowTaskCodes.Run, WorkflowTaskCodes.Extra)
            .Register()
            .AddTask<StartRunWorkflowTaskDefinition>(WorkflowTaskCodes.Run)
            .RequiresCompleted(WorkflowTaskCodes.Preset)
            .ConflictsWith(WorkflowTaskCodes.Preset)
            .Register()
            .AddTask<RunExtraWorkflowTaskDefinition>(WorkflowTaskCodes.Extra)
            .RequiresCompleted(WorkflowTaskCodes.Preset)
            .ConflictsWith(WorkflowTaskCodes.Preset)
            .Register();

        return builder.Build();
    }
}
