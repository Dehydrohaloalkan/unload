using Microsoft.Extensions.DependencyInjection;

namespace Unload.Run.Application;

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
        services.AddSingleton<IRunRequestFactory, RunRequestFactory>();
        services.AddSingleton<IRunOrchestrator>(_ => new RunOrchestrator(
            _.GetRequiredService<IRunRequestFactory>(),
            _.GetRequiredService<IRunCoordinator>(),
            _.GetRequiredService<IRunStateStore>(),
            outputDirectory));
        return services;
    }
}
