using Microsoft.Extensions.Configuration;
using Unload.Tasks;
using Unload.Tasks.MainUnload;

namespace Unload.Bootstrapper;

/// <summary>
/// Загружает и собирает <see cref="UnloadConfiguration"/> из <see cref="IConfiguration"/>.
/// Инкапсулирует резолв корня workspace и биндинг всех секций конфига.
/// </summary>
public static class UnloadConfigurationLoader
{
    /// <summary>
    /// Загружает конфигурацию приложения: резолвит корень workspace и биндит все секции.
    /// </summary>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <returns>Собранный <see cref="UnloadConfiguration"/>.</returns>
    /// <exception cref="DirectoryNotFoundException">Корень workspace не найден.</exception>
    /// <exception cref="InvalidOperationException">Обязательная секция конфига отсутствует.</exception>
    public static UnloadConfiguration Load(IConfiguration configuration)
    {
        var root = ResolveWorkspaceRoot();

        var paths = new UnloadRuntimePaths(
            CatalogPath: Path.Combine(root, "configs", "catalog.json"),
            ScriptsDirectory: Path.Combine(root, "scripts"),
            OutputDirectory: Path.Combine(root, "output"));

        var database = configuration
            .GetSection(DatabaseRuntimeSettings.SectionName)
            .Get<DatabaseRuntimeSettings>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{DatabaseRuntimeSettings.SectionName}' is required.");

        var runner = configuration.GetSection("Runner").Get<RunnerOptions>()
            ?? new RunnerOptions(ChunkSizeBytes: 10 * 1024 * 1024, WorkerCount: 4);

        var presetGate = configuration.GetSection("PresetGate").Get<PresetGateOptions>()
            ?? PresetGateOptions.Default;

        var historyRetention = configuration
            .GetSection(HistoryRetentionOptions.SectionName)
            .Get<HistoryRetentionOptions>()
            ?? HistoryRetentionOptions.Default;

        var extra = configuration
            .GetSection(ExtraUnloadSettings.SectionName)
            .Get<ExtraUnloadSettings>()
            ?? ExtraUnloadSettings.Default;

        return new UnloadConfiguration(paths, database, runner, presetGate, historyRetention, extra);
    }

    private static string ResolveWorkspaceRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            var catalog = Path.Combine(current.FullName, "configs", "catalog.json");
            var scripts = Path.Combine(current.FullName, "scripts");

            if (File.Exists(catalog) && Directory.Exists(scripts))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Workspace root not found. Expected folders: 'configs' with 'catalog.json' and 'scripts'.");
    }
}
