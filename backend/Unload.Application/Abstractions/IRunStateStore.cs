using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Контракт хранилища статусов запусков.
/// Используется API и background worker для чтения и обновления жизненного цикла выполнения.
/// </summary>
public interface IRunStateStore
{
    /// <summary>
    /// Помечает запуск как поставленный в очередь.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="targetCodes">Target-коды, связанные с запуском.</param>
    void SetQueued(string correlationId, IReadOnlyCollection<string> targetCodes);

    /// <summary>
    /// Помечает запуск как выполняющийся.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    void SetRunning(string correlationId);

    /// <summary>
    /// Применяет событие раннера к текущему состоянию запуска.
    /// </summary>
    /// <param name="event">Событие выполнения, содержащее шаг и сообщение.</param>
    void ApplyEvent(RunnerEvent @event);

    /// <summary>
    /// Помечает запуск как завершившийся с ошибкой.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="message">Текст ошибки.</param>
    void SetFailed(string correlationId, string message);

    /// <summary>
    /// Возвращает статус конкретного запуска.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <returns>Текущий статус запуска или <c>null</c>, если запуск не найден.</returns>
    RunStatusInfo? Get(string correlationId);

    /// <summary>
    /// Возвращает список всех запусков с текущими статусами.
    /// </summary>
    /// <returns>Снимок списка запусков, обычно отсортированный по времени обновления.</returns>
    IReadOnlyList<RunStatusInfo> List();
}
