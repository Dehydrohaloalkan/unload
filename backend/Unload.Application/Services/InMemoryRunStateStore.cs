using System.Collections.Concurrent;
using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Потокобезопасное in-memory хранилище статусов запусков.
/// Используется API и background worker для синхронизации жизненного цикла run.
/// </summary>
public class InMemoryRunStateStore : IRunStateStore
{
    private readonly ConcurrentDictionary<string, RunStatusInfo> _runs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Создает или перезаписывает запись запуска в статусе выполнения.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="targetCodes">Target-коды запуска.</param>
    public void SetStarted(string correlationId, IReadOnlyCollection<string> targetCodes)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new RunStatusInfo(
            correlationId,
            RunLifecycleStatus.Running,
            targetCodes.ToArray(),
            now,
            now,
            Message: "Run started.");

        _runs[correlationId] = snapshot;
    }

    /// <summary>
    /// Обновляет запись запуска в статус выполняется.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    public void SetRunning(string correlationId)
    {
        var now = DateTimeOffset.UtcNow;
        _runs.AddOrUpdate(
            correlationId,
            _ => new RunStatusInfo(
                correlationId,
                RunLifecycleStatus.Running,
                Array.Empty<string>(),
                now,
                now,
                Message: "Run started."),
            (_, current) => current with
            {
                Status = RunLifecycleStatus.Running,
                UpdatedAt = now,
                Message = "Run started."
            });
    }

    /// <summary>
    /// Применяет входящее событие раннера к снимку состояния запуска.
    /// </summary>
    /// <param name="event">Событие, на основании которого обновляется статус.</param>
    public void ApplyEvent(RunnerEvent @event)
    {
        var now = DateTimeOffset.UtcNow;
        _runs.AddOrUpdate(
            @event.CorrelationId,
            _ => new RunStatusInfo(
                @event.CorrelationId,
                MapStatus(@event.Step),
                Array.Empty<string>(),
                now,
                now,
                @event.Step,
                @event.Message,
                @event.FilePath),
            (_, current) => current with
            {
                Status = MapStatus(@event.Step),
                UpdatedAt = now,
                LastStep = @event.Step,
                Message = @event.Message,
                OutputPath = @event.Step == RunnerStep.Completed ? @event.FilePath : current.OutputPath
            });
    }

    /// <summary>
    /// Помечает запуск как завершившийся ошибкой.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="message">Диагностическое сообщение об ошибке.</param>
    public void SetFailed(string correlationId, string message)
    {
        var now = DateTimeOffset.UtcNow;
        _runs.AddOrUpdate(
            correlationId,
            _ => new RunStatusInfo(
                correlationId,
                RunLifecycleStatus.Failed,
                Array.Empty<string>(),
                now,
                now,
                RunnerStep.Failed,
                message),
            (_, current) => current with
            {
                Status = RunLifecycleStatus.Failed,
                UpdatedAt = now,
                LastStep = RunnerStep.Failed,
                Message = message
            });
    }

    /// <summary>
    /// Возвращает текущее состояние указанного запуска.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <returns>Состояние запуска или <c>null</c>, если запись отсутствует.</returns>
    public RunStatusInfo? Get(string correlationId)
    {
        return _runs.TryGetValue(correlationId, out var run) ? run : null;
    }

    /// <summary>
    /// Возвращает список всех запусков, отсортированный по времени обновления.
    /// </summary>
    /// <returns>Снимок состояний запусков.</returns>
    public IReadOnlyList<RunStatusInfo> List()
    {
        return _runs.Values
            .OrderByDescending(static x => x.UpdatedAt)
            .ToArray();
    }

    /// <summary>
    /// Преобразует шаг раннера в агрегированный статус жизненного цикла.
    /// </summary>
    /// <param name="step">Шаг выполнения раннера.</param>
    /// <returns>Агрегированный статус для отображения клиентам.</returns>
    private static RunLifecycleStatus MapStatus(RunnerStep step)
    {
        return step switch
        {
            RunnerStep.Completed => RunLifecycleStatus.Completed,
            RunnerStep.Failed => RunLifecycleStatus.Failed,
            _ => RunLifecycleStatus.Running
        };
    }
}
