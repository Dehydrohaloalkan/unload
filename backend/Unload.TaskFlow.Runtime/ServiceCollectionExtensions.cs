using Microsoft.Extensions.DependencyInjection;
using Unload.TaskFlow;

namespace Unload.TaskFlow.Runtime;

/// <summary>
/// Регистрация in-memory runtime сервисов task pipeline.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует in-memory access/state компоненты task orchestration.
    /// </summary>
    public static IServiceCollection AddUnloadTaskFlowRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowStageStateStore, InMemoryWorkflowStageStateStore>();
        services.AddSingleton<IWorkflowTaskAccessService, InMemoryWorkflowTaskAccessService>();
        return services;
    }
}
