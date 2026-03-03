namespace Unload.Application;

/// <summary>
/// Контракт use-case слоя запуска выгрузки.
/// Используется транспортными слоями (API/Console) для постановки нового запуска в очередь.
/// </summary>
public interface IRunOrchestrator
{
    /// <summary>
    /// Создает и ставит запуск в очередь.
    /// </summary>
    /// <param name="targetCodes">Target-коды, выбранные пользователем.</param>
    /// <returns>Идентификатор корреляции созданного запуска.</returns>
    string StartRun(IReadOnlyCollection<string> targetCodes);
}
