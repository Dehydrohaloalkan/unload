namespace Unload.Application;

/// <summary>
/// Контракт дополнительных задач выгрузки, не завязанных на каталог target-кодов.
/// </summary>
public interface IScriptTaskOrchestrator
{
    /// <summary>
    /// Выполняет SQL-скрипты из папки <c>scripts/preset</c>.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат запуска preset-задачи.</returns>
    Task<ScriptTaskRunResult> RunPresetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Выполняет SQL-скрипты из корня <c>scripts</c> (без подпапок), группирует результат по
    /// колонке <c>NrBank</c> и пишет строки <c>LineFile</c> в выходные файлы.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат запуска доп-выгрузки скриптов.</returns>
    Task<ScriptTaskRunResult> RunExtraAsync(CancellationToken cancellationToken);
}
