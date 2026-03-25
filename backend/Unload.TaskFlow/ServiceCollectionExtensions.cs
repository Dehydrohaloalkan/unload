using Microsoft.Extensions.DependencyInjection;
using Unload.Workflow;

namespace Unload.TaskFlow;

/// <summary>
/// Регистрация task/workflow orchestration слоя.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует сервисы task orchestration и настраивает прозрачный pipeline.
    /// </summary>
    public static IServiceCollection AddUnloadTaskFlow(
        this IServiceCollection services,
        PresetGateOptions presetGateOptions)
    {
        var pipeline = TaskPipelineConfigurator.BuildDefault();

        services.AddSingleton(presetGateOptions);
        services.AddSingleton<IPresetGateService, PresetGateService>();
        services.AddSingleton<IPresetProbeService, PresetProbeService>();
        services.AddSingleton(pipeline);
        services.AddSingleton<IWorkflowTaskDependencyCatalog, WorkflowTaskDependencyCatalog>();
        services.AddSingleton<IWorkflowTaskTransitionService, WorkflowTaskTransitionService>();

        foreach (var task in pipeline.Tasks)
        {
            services.AddSingleton(typeof(IWorkflowTaskDefinition), task.DefinitionType);

            foreach (var transitionHandlerType in task.TransitionHandlerTypes)
            {
                services.AddSingleton(typeof(IWorkflowTaskTransitionHandler), transitionHandlerType);
            }
        }

        return services;
    }
}
