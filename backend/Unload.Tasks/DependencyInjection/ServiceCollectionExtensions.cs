using Microsoft.Extensions.DependencyInjection;

namespace Unload.Tasks.DependencyInjection;

/// <summary>
/// Регистрация ядра оркестрации задач выгрузки.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="TaskWorkflow"/>, <see cref="DailyWindowPolicy"/>,
    /// <see cref="RunActivationChannel"/> и вспомогательные сервисы ядра.
    /// Конкретные задачи (<see cref="UnloadTask"/>) регистрируются из проектов задач
    /// (например, <c>AddUnloadMainUnload</c>).
    /// </summary>
    public static IServiceCollection AddUnloadTasks(
        this IServiceCollection services,
        PresetGateOptions presetGateOptions)
    {
        services.AddSingleton(presetGateOptions);
        services.AddSingleton<DailyWindowPolicy>();
        services.AddSingleton<IPresetProbeService, PresetProbeService>();
        services.AddSingleton<RunActivationChannel>();

        services.AddSingleton<PresetTask>();

        services.AddSingleton<UnloadTask>(static sp => sp.GetRequiredService<PresetTask>());

        services.AddSingleton<TaskWorkflow>();

        return services;
    }
}
