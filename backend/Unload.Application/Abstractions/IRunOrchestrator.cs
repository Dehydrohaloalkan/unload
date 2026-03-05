namespace Unload.Application;

/// <summary>
/// Контракт use-case слоя запуска выгрузки.
/// Используется транспортными слоями (API/Console) для запуска новой выгрузки.
/// </summary>
public interface IRunOrchestrator
{
    /// <summary>
    /// Создает и запускает выгрузку.
    /// </summary>
    /// <param name="targetCodes">Target-коды, выбранные пользователем.</param>
    /// <param name="memberNames">Имена мемберов, выбранных пользователем.</param>
    /// <returns>Идентификатор корреляции созданного запуска.</returns>
    string StartRun(IReadOnlyCollection<string> targetCodes, IReadOnlyCollection<string>? memberNames = null);
}
