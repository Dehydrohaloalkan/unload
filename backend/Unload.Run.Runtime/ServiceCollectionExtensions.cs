using Microsoft.Extensions.DependencyInjection;
using Unload.Core;
using Unload.Run.Application;
using Unload.Workflow;

namespace Unload.Run.Runtime;

/// <summary>
/// Регистрация in-memory runtime сервисов основного run-пайплайна.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует runtime-компоненты активного запуска и store статусов.
    /// </summary>
    public static IServiceCollection AddUnloadRunRuntime(
        this IServiceCollection services,
        int workerCount,
        string stateFilePath)
    {
        services.AddSingleton<ISingleActiveWorkflow<RunRequest>, InMemorySingleActiveWorkflow<RunRequest>>();
        services.AddSingleton<IRunCoordinator, InMemoryRunCoordinator>();
        services.AddSingleton<IRunStateStore>(_ => new InMemoryRunStateStore(workerCount, stateFilePath));
        services.AddSingleton<IMqSenderFeedbackConsumer, MqSenderFeedbackConsumer>();
        return services;
    }
}
