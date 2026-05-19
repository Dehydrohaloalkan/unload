using Unload.Tasks;
using Unload.Tasks.MainUnload;

namespace Unload.Bootstrapper;

/// <summary>
/// Агрегат всех runtime-настроек приложения Unload.
/// Загружается через <see cref="UnloadConfigurationLoader"/> и регистрируется как singleton.
/// </summary>
public record UnloadConfiguration(
    UnloadRuntimePaths Paths,
    DatabaseRuntimeSettings Database,
    RunnerOptions Runner,
    PresetGateOptions PresetGate,
    HistoryRetentionOptions HistoryRetention);
