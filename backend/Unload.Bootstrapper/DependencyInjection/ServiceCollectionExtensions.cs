using Microsoft.Extensions.DependencyInjection;
using Unload.Catalog;
using Unload.Core;
using Unload.Cryptography;
using Unload.DataBase;
using Unload.FileWriter;
using Unload.MQ;
using Unload.Run.Application.DependencyInjection;
using Unload.Run.Runtime.DependencyInjection;
using Unload.Runner;
using Unload.ScriptTasks.DependencyInjection;
using Unload.TaskFlow;
using Unload.TaskFlow.DependencyInjection;
using Unload.TaskFlow.Runtime.DependencyInjection;
using Unload.Workflow;

namespace Unload.Bootstrapper.DependencyInjection;

/// <summary>
/// Расширения DI-контейнера для регистрации runtime сервисов выгрузки.
/// Используется API и Console при инициализации приложения.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует полный набор инфраструктурных и application сервисов выгрузки.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="paths">Пути к каталогу, скриптам и output.</param>
    /// <returns>Та же коллекция сервисов для цепочки вызовов.</returns>
    public static IServiceCollection AddUnloadRuntime(
        this IServiceCollection services,
        UnloadRuntimePaths paths,
        DatabaseRuntimeSettings databaseSettings,
        RunnerOptions? runnerOptions = null,
        PresetGateOptions? presetGateOptions = null)
    {
        ArgumentNullException.ThrowIfNull(databaseSettings);
        if (databaseSettings.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Database timeout must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(databaseSettings.ConnectionString))
        {
            throw new InvalidOperationException("Database connection string is required.");
        }

        services.AddSingleton<ICatalogService>(_ => new JsonCatalogService(paths.CatalogPath, paths.ScriptsDirectory));
        services.AddSingleton<IDatabaseClientFactory>(_ => new DatabaseClientFactory(
            databaseSettings.TimeoutSeconds,
            databaseSettings.ConnectionString));
        services.AddSingleton<IFileChunkWriter, PipeSeparatedFileChunkWriter>();
        services.AddSingleton<InMemoryMqPublisher>();
        services.AddSingleton<IMqPublisher>(static x => x.GetRequiredService<InMemoryMqPublisher>());
        services.AddSingleton<IMqFileBatchSource>(static x => x.GetRequiredService<InMemoryMqPublisher>());
        services.AddSingleton<IMqSenderFeedbackSource>(static x => x.GetRequiredService<InMemoryMqPublisher>());
        services.AddHostedService<SenderStubMqBackgroundService>();
        services.AddSingleton<IRequestHasher, Sha256RequestHasher>();
        services.AddSingleton<IWorkflowTaskDispatcher, WorkflowTaskDispatcher>();
        services.AddSingleton<IWorkflowTaskRegistry, WorkflowTaskRegistry>();
        var opts = runnerOptions ?? new RunnerOptions(ChunkSizeBytes: 10 * 1024 * 1024, WorkerCount: 4);
        var stateDirectory = Path.Combine(paths.OutputDirectory, "_state");
        var runStateFilePath = Path.Combine(stateDirectory, "runs.json");
        services.AddSingleton(opts);
        services.AddSingleton<IRunner, RunnerEngine>();
        services.AddUnloadRunRuntime(opts.WorkerCount, runStateFilePath);
        services.AddUnloadRunApplication(paths.OutputDirectory);
        services.AddUnloadTaskFlow(presetGateOptions ?? PresetGateOptions.Default);
        services.AddUnloadTaskFlowRuntime();
        services.AddUnloadScriptTasksInfrastructure(paths.ScriptsDirectory, paths.OutputDirectory);

        return services;
    }
}
