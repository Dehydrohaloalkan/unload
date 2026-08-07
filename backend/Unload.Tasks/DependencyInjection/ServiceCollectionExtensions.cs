using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        // Хост может заранее зарегистрировать управляемое время; runtime по умолчанию использует системное.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DailyWindowPolicy>();
        services.AddSingleton<PresetCompletionRecovery>();
        services.AddSingleton<RunActivationChannel>();
        services.AddSingleton<ExtraActivationChannel>();

        services.AddSingleton<TaskWorkflow>();
        services.AddSingleton<WorkflowQueryService>();

        return services;
    }
}
