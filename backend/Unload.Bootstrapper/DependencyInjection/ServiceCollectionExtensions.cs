using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Unload.Catalog;
using Unload.Core;
using Unload.Cryptography;
using Unload.DataBase;
using Unload.FileWriter;
using Unload.Gateway;
using Unload.Store;
using Unload.Tasks.DependencyInjection;
using Unload.Tasks.ExtraUnload.DependencyInjection;
using Unload.Tasks.MainUnload.DependencyInjection;
using Unload.Tasks.Preset.DependencyInjection;

namespace Unload.Bootstrapper.DependencyInjection;

/// <summary>
/// Расширения DI-контейнера для регистрации runtime сервисов выгрузки.
/// Используется API и Console при инициализации приложения.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Загружает конфигурацию и регистрирует полный набор инфраструктурных и application сервисов выгрузки.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <returns>Та же коллекция сервисов для цепочки вызовов.</returns>
    public static IServiceCollection AddUnloadRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var config = UnloadConfigurationLoader.Load(configuration);

        var databaseSettings = config.Database;

        if (databaseSettings.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Database timeout must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(databaseSettings.ConnectionString))
        {
            throw new InvalidOperationException("Database connection string is required.");
        }

        services.AddSingleton(config);
        services.AddSingleton(config.Paths);
        services.AddSingleton(config.HistoryRetention);

        services.AddSingleton<ICatalogService>(_ => new JsonCatalogService(config.Paths.CatalogPath, config.Paths.ScriptsDirectory));
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

        var stateDirectory = Path.Combine(config.Paths.OutputDirectory, "_state");
        var runStateFilePath = Path.Combine(stateDirectory, "runs.json");
        var taskHistoryFilePath = Path.Combine(stateDirectory, "task-history.json");

        services.AddSingleton(config.Runner);
        services.AddSingleton<RunStateStore>(_ => new RunStateStore(config.Runner.WorkerCount, runStateFilePath));
        services.AddSingleton<TaskExecutionHistoryStore>(_ => new TaskExecutionHistoryStore(taskHistoryFilePath));
        services.AddSingleton<IGatewaySenderFeedbackConsumer, GatewaySenderFeedbackConsumer>();
        services.AddSingleton<RequeueService>();

        var uploadOptions = new GatewayUploadOptions(Path.Combine(config.Paths.OutputDirectory, "_uploads"));
        services.AddSingleton(uploadOptions);
        services.AddSingleton<GatewayUploadService>();

        services.AddUnloadTasks(config.PresetGate);
        services.AddUnloadMainUnload(config.Paths.OutputDirectory);
        services.AddUnloadExtraUnload(config.Paths.ScriptsDirectory, config.Paths.OutputDirectory);
        services.AddUnloadPreset(config.Paths.ScriptsDirectory);

        return services;
    }
}
