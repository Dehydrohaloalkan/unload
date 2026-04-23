using Microsoft.Extensions.DependencyInjection;

namespace Unload.Run.Application.DependencyInjection;

/// <summary>
/// Регистрация use-case слоя основного run-пайплайна.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует сервисы orchestration слоя основного запуска.
    /// </summary>
    public static IServiceCollection AddUnloadRunApplication(
        this IServiceCollection services,
        string outputDirectory)
    {
        services.AddSingleton(new RunApplicationOptions(outputDirectory));
        services.AddSingleton<IRunRequestFactory, RunRequestFactory>();
        return services;
    }
}
