using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Контракт фабрики формирования <see cref="RunRequest"/>.
/// Используется orchestrator для централизованной генерации идентификатора запуска и параметров.
/// </summary>
public interface IRunRequestFactory
{
    /// <summary>
    /// Создает новый объект запроса выполнения выгрузки.
    /// </summary>
    /// <param name="targetCodes">Нормализованные target-коды для запуска.</param>
    /// <param name="outputDirectory">Базовая директория, где нужно сохранять результаты.</param>
    /// <returns>Готовый запрос выполнения раннера.</returns>
    RunRequest Create(IReadOnlyCollection<string> targetCodes, string outputDirectory);
}
