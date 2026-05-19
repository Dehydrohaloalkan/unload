using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Unload.ScriptTasks.Abstractions;
using Unload.Tasks;

namespace Unload.ScriptTasks.DependencyInjection;

/// <summary>
/// Регистрация инфраструктурных реализаций task executor-ов.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует SQL-based реализации preset orchestration.
    /// </summary>
    public static IServiceCollection AddUnloadScriptTasksInfrastructure(
        this IServiceCollection services,
        string scriptsDirectory)
    {
        services.AddSingleton<IScriptTaskEventPublisher, ScriptTaskEventPublisher>();
        services.AddSingleton<IPresetScriptExecutor, PresetScriptExecutor>();
        services.AddSingleton<IScriptTaskOrchestrator>(_ => new ScriptTaskOrchestrator(
            scriptsDirectory,
            _.GetRequiredService<IPresetScriptExecutor>(),
            _.GetRequiredService<IScriptTaskEventPublisher>(),
            _.GetRequiredService<ILogger<ScriptTaskOrchestrator>>()));
        return services;
    }
}
