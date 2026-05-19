using Microsoft.Extensions.DependencyInjection;
using Unload.Tasks;

namespace Unload.Tasks.MainUnload.DependencyInjection;

/// <summary>
/// Регистрация сервисов основной задачи выгрузки.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="RunApplicationOptions"/>, <see cref="RunRequestFactory"/>,
    /// <see cref="MainUnloadEngine"/> и <see cref="MainUnloadTask"/>.
    /// </summary>
    public static IServiceCollection AddUnloadMainUnload(
        this IServiceCollection services,
        string outputDirectory)
    {
        services.AddSingleton(new RunApplicationOptions(outputDirectory));
        services.AddSingleton<RunRequestFactory>();
        services.AddSingleton<MainUnloadEngine>();
        services.AddSingleton<MainUnloadTask>();
        services.AddSingleton<UnloadTask>(static sp => sp.GetRequiredService<MainUnloadTask>());
        return services;
    }
}
