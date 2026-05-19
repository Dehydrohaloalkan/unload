using Microsoft.Extensions.DependencyInjection;
using Unload.Tasks;

namespace Unload.Tasks.ExtraUnload.DependencyInjection;

/// <summary>
/// Регистрация сервисов задачи extra-выгрузки.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="ExtraUnloadOptions"/>, <see cref="ExtraScriptExecutor"/>,
    /// <see cref="ExtraOutputWriter"/> и <see cref="ExtraUnloadTask"/>.
    /// </summary>
    public static IServiceCollection AddUnloadExtraUnload(
        this IServiceCollection services,
        string scriptsDirectory,
        string outputDirectory)
    {
        services.AddSingleton(new ExtraUnloadOptions(scriptsDirectory, outputDirectory));
        services.AddSingleton<ExtraScriptExecutor>();
        services.AddSingleton<ExtraOutputWriter>();
        services.AddSingleton<ExtraUnloadTask>();
        services.AddSingleton<UnloadTask>(static sp => sp.GetRequiredService<ExtraUnloadTask>());
        return services;
    }
}
