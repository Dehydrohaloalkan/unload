using Microsoft.Extensions.DependencyInjection;
using Unload.Catalog;
using Unload.Core;
using Unload.Cryptography;
using Unload.DataBase;
using Unload.FileWriter;
using Microsoft.Extensions.Configuration;
using Unload.Gateway;
using Unload.Store;
using Unload.Tasks;
using Unload.Tasks.DependencyInjection;
using Unload.Tasks.ExtraUnload.DependencyInjection;
using Unload.Tasks.MainUnload;
using Unload.Tasks.Preset.DependencyInjection;
using Unload.Tasks.MainUnload.DependencyInjection;

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
        IConfiguration configuration,
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
        services.Configure<GatewayOptions>(configuration.GetSection(GatewayOptions.SectionName));
        services.AddSingleton<FtpGatewayPublisher>();
        services.AddSingleton<IGatewayPublisher>(static x => x.GetRequiredService<FtpGatewayPublisher>());
        services.AddSingleton<IGatewayBatchSource>(static x => x.GetRequiredService<FtpGatewayPublisher>());
        services.AddSingleton<IGatewaySenderFeedbackSource>(static x => x.GetRequiredService<FtpGatewayPublisher>());
        services.AddHostedService<FtpGatewayBackgroundService>();
        services.AddSingleton<IRequestHasher, Sha256RequestHasher>();
        var opts = runnerOptions ?? new RunnerOptions(ChunkSizeBytes: 10 * 1024 * 1024, WorkerCount: 4);
        var stateDirectory = Path.Combine(paths.OutputDirectory, "_state");
        var runStateFilePath = Path.Combine(stateDirectory, "runs.json");
        var taskHistoryFilePath = Path.Combine(stateDirectory, "task-history.json");
        services.AddSingleton(opts);
        services.AddSingleton<RunStateStore>(_ => new RunStateStore(opts.WorkerCount, runStateFilePath));
        services.AddSingleton<TaskExecutionHistoryStore>(_ => new TaskExecutionHistoryStore(taskHistoryFilePath));
        services.AddSingleton<IGatewaySenderFeedbackConsumer, GatewaySenderFeedbackConsumer>();
        services.AddUnloadTasks(presetGateOptions ?? PresetGateOptions.Default);
        services.AddUnloadMainUnload(paths.OutputDirectory);
        services.AddUnloadExtraUnload(paths.ScriptsDirectory, paths.OutputDirectory);
        services.AddUnloadPreset(paths.ScriptsDirectory);

        return services;
    }
}
