using Microsoft.Extensions.DependencyInjection;
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
    public static IServiceCollection AddUnloadRuntime(this IServiceCollection services, UnloadRuntimePaths paths)
    {
        services.AddSingleton<ICatalogService>(_ => new JsonCatalogService(paths.CatalogPath, paths.ScriptsDirectory));
        services.AddSingleton<IDatabaseClient, StubDatabaseClient>();
        services.AddSingleton<IFileChunkWriter, PipeSeparatedFileChunkWriter>();
        services.AddSingleton<IMqPublisher, InMemoryMqPublisher>();
        services.AddSingleton<IRequestHasher, Sha256RequestHasher>();
        services.AddSingleton(new RunnerOptions(
            ChunkSizeBytes: 10 * 1024 * 1024,
            MaxDegreeOfParallelism: Math.Max(Environment.ProcessorCount / 2, 1),
            FileWriterDegreeOfParallelism: 4,
            QueuePublisherDegreeOfParallelism: 1,
            DataflowBoundedCapacity: 8));
        services.AddSingleton<IRunner, RunnerEngine>();
        services.AddSingleton<IRunRequestFactory, RunRequestFactory>();
        services.AddSingleton<IRunQueue, InMemoryRunQueue>();
        services.AddSingleton<IRunStateStore, InMemoryRunStateStore>();
        services.AddSingleton<IRunOrchestrator>(_ => new RunOrchestrator(
            _.GetRequiredService<IRunRequestFactory>(),
            _.GetRequiredService<IRunQueue>(),
            _.GetRequiredService<IRunStateStore>(),
            paths.OutputDirectory));

        return services;
    }
}
