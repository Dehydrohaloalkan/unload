using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Unload.Catalog;
using Unload.Core;
using Unload.Cryptography;
using Unload.DataBase;
using Unload.FileWriter;
using Unload.MQ;
using Unload.Runner;

namespace Unload.Application;

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
        DatabaseRuntimeSettings? databaseSettings = null,
        RunnerOptions? runnerOptions = null)
    {
        var dbSettings = databaseSettings ?? throw new InvalidOperationException(
            $"Database settings are required. Configure section '{DatabaseRuntimeSettings.SectionName}' in appsettings.");
        if (dbSettings.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Database timeout must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(dbSettings.ConnectionString))
        {
            throw new InvalidOperationException("Database connection string is required.");
        }

        services.AddSingleton<ICatalogService>(_ => new JsonCatalogService(paths.CatalogPath, paths.ScriptsDirectory));
        services.AddSingleton<IDatabaseClientFactory>(_ => new DatabaseClientFactory(
            dbSettings.TimeoutSeconds,
            dbSettings.ConnectionString));
        services.AddSingleton<IFileChunkWriter, PipeSeparatedFileChunkWriter>();
        services.AddSingleton<IMqPublisher, InMemoryMqPublisher>();
        services.AddSingleton<IRequestHasher, Sha256RequestHasher>();
        var opts = runnerOptions ?? new RunnerOptions(ChunkSizeBytes: 10 * 1024 * 1024, WorkerCount: 4);
        services.AddSingleton(opts);
        services.AddSingleton<IRunner, RunnerEngine>();
        services.AddSingleton<IRunRequestFactory, RunRequestFactory>();
        services.AddSingleton<IRunCoordinator, InMemoryRunCoordinator>();
        services.AddSingleton<IRunStateStore, InMemoryRunStateStore>();
        services.AddSingleton<IRunOrchestrator>(_ => new RunOrchestrator(
            _.GetRequiredService<IRunRequestFactory>(),
            _.GetRequiredService<IRunCoordinator>(),
            _.GetRequiredService<IRunStateStore>(),
            paths.OutputDirectory));
        services.AddSingleton<IScriptTaskOrchestrator>(_ => new ScriptTaskOrchestrator(
            paths.ScriptsDirectory,
            paths.OutputDirectory,
            _.GetRequiredService<IDatabaseClientFactory>(),
            _.GetRequiredService<IMqPublisher>(),
            _.GetRequiredService<ILogger<ScriptTaskOrchestrator>>()));

        return services;
    }
}
