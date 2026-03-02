using Microsoft.Extensions.DependencyInjection;
using Unload.Catalog;
using Unload.Core;
using Unload.Cryptography;
using Unload.DataBase;
using Unload.FileWriter;
using Unload.MQ;
using Unload.Runner;

namespace Unload.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUnloadRuntime(this IServiceCollection services, UnloadRuntimePaths paths)
    {
        services.AddSingleton<ICatalogService>(_ => new JsonCatalogService(paths.CatalogPath, paths.ScriptsDirectory));
        services.AddSingleton<IDatabaseClient, StubDatabaseClient>();
        services.AddSingleton<IFileChunkWriter, PipeSeparatedFileChunkWriter>();
        services.AddSingleton<IMqPublisher, InMemoryMqPublisher>();
        services.AddSingleton<IRunDiagnosticsSink>(_ => new CsvRunDiagnosticsSink(paths.DiagnosticsDirectory));
        services.AddSingleton<IRequestHasher, Sha256RequestHasher>();
        services.AddSingleton(new RunnerOptions(
            ChunkSizeBytes: 10 * 1024 * 1024,
            MaxDegreeOfParallelism: Math.Max(Environment.ProcessorCount / 2, 1),
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
