using Microsoft.Extensions.DependencyInjection;
using Unload.Core;

namespace Unload.Tasks.DependencyInjection;

/// <summary>
/// Регистрация ядра оркестрации задач выгрузки.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="TaskWorkflow"/>, <see cref="DailyWindowPolicy"/>,
    /// все три задачи как <see cref="UnloadTask"/> и <see cref="IPresetProbeService"/>.
    /// <see cref="IScriptTaskOrchestrator"/> регистрируется отдельно из ScriptTasks DI.
    /// </summary>
    public static IServiceCollection AddUnloadTasks(
        this IServiceCollection services,
        PresetGateOptions presetGateOptions)
    {
        services.AddSingleton(presetGateOptions);
        services.AddSingleton<DailyWindowPolicy>();
        services.AddSingleton<IPresetProbeService, PresetProbeService>();
        services.AddSingleton<ISingleActiveWorkflow<RunRequest>, InMemorySingleActiveWorkflow<RunRequest>>();

        // Задачи регистрируются как конкретные типы и как IEnumerable<UnloadTask>.
        services.AddSingleton<MainUnloadTask>();
        services.AddSingleton<PresetTask>();
        services.AddSingleton<ExtraUnloadTask>();

        services.AddSingleton<UnloadTask>(static sp => sp.GetRequiredService<MainUnloadTask>());
        services.AddSingleton<UnloadTask>(static sp => sp.GetRequiredService<PresetTask>());
        services.AddSingleton<UnloadTask>(static sp => sp.GetRequiredService<ExtraUnloadTask>());

        services.AddSingleton<TaskWorkflow>();

        return services;
    }
}
