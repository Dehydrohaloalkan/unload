namespace Unload.Tasks;

/// <summary>
/// Контракт preset-задачи выгрузки, не завязанной на каталог target-кодов.
/// Реализация живёт в <c>Unload.ScriptTasks</c>; удаляется в Фазе 5.
/// </summary>
public interface IScriptTaskOrchestrator
{
    /// <summary>
    /// Выполняет SQL-скрипты из папки <c>scripts/preset</c>.
    /// </summary>
    Task<ScriptTaskRunResult> RunPresetAsync(CancellationToken cancellationToken);
}
