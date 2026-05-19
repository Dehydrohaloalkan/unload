using Microsoft.Extensions.DependencyInjection;
using Unload.Tasks;

namespace Unload.Tasks.Preset.DependencyInjection;

/// <summary>
/// Регистрация preset- и probe-задач выгрузки.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="PresetTask"/>, <see cref="ProbeTask"/> и <see cref="PresetScriptExecutor"/>.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="scriptsDirectory">Путь к директории со скриптами.</param>
    public static IServiceCollection AddUnloadPreset(
        this IServiceCollection services,
        string scriptsDirectory)
    {
        services.AddSingleton(new PresetTaskOptions(scriptsDirectory));
        services.AddSingleton<PresetScriptExecutor>();

        services.AddSingleton<PresetTask>();
        services.AddSingleton<UnloadTask>(static sp => sp.GetRequiredService<PresetTask>());

        services.AddSingleton<ProbeTask>();
        services.AddSingleton<UnloadTask>(static sp => sp.GetRequiredService<ProbeTask>());

        return services;
    }
}
