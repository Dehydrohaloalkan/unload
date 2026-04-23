using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Unload.ScriptTasks.Abstractions;
using Unload.TaskFlow;

namespace Unload.ScriptTasks.DependencyInjection;

/// <summary>
/// Регистрация инфраструктурных реализаций task executor-ов.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует SQL/file-based реализации task orchestration.
    /// </summary>
    public static IServiceCollection AddUnloadScriptTasksInfrastructure(
        this IServiceCollection services,
        string scriptsDirectory,
        string outputDirectory)
    {
        services.AddSingleton<IScriptTaskEventPublisher, ScriptTaskEventPublisher>();
        services.AddSingleton<IPresetScriptExecutor, PresetScriptExecutor>();
        services.AddSingleton<IExtraScriptExecutor, ExtraScriptExecutor>();
        services.AddSingleton<IExtraOutputWriter, ExtraOutputWriter>();
        services.AddSingleton<IScriptTaskOrchestrator>(_ => new ScriptTaskOrchestrator(
            scriptsDirectory,
            outputDirectory,
            _.GetRequiredService<IPresetScriptExecutor>(),
            _.GetRequiredService<IExtraScriptExecutor>(),
            _.GetRequiredService<IExtraOutputWriter>(),
            _.GetRequiredService<IScriptTaskEventPublisher>(),
            _.GetRequiredService<ILogger<ScriptTaskOrchestrator>>()));
        return services;
    }
}
