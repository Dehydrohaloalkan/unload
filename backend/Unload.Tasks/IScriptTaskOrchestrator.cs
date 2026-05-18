namespace Unload.Tasks;

/// <summary>
/// Контракт дополнительных задач выгрузки, не завязанных на каталог target-кодов.
/// Реализация живёт в <c>Unload.ScriptTasks</c>; удаляется в Фазах 4–5.
/// </summary>
public interface IScriptTaskOrchestrator
{
    /// <summary>
    /// Выполняет SQL-скрипты из папки <c>scripts/preset</c>.
    /// </summary>
    Task<ScriptTaskRunResult> RunPresetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Выполняет SQL-скрипты из корня <c>scripts</c> (без подпапок),
    /// группирует результат по колонке <c>NrBank</c>
    /// и пишет строки <c>LineFile</c> в выходные файлы.
    /// </summary>
    Task<ScriptTaskRunResult> RunExtraAsync(bool publishToGateway, CancellationToken cancellationToken);
}
