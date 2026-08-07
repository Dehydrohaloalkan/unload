using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.Store;

/// <summary>
/// Потокобезопасное in-memory хранилище статусов запусков.
/// Используется API и background worker для синхронизации жизненного цикла run.
/// </summary>
public class RunStateStore
{
    private const string PersistenceVersion = "1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private const string TaskCodeRun = "run";
    private readonly ConcurrentDictionary<string, RunStatusInfo> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonFileStore<RunStatePersistenceSnapshot> _store;
    private readonly RunStateProjector _projector;

    public RunStateStore(
        int workerCount,
        string stateFilePath,
        ILogger<RunStateStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateFilePath);
        _projector = new RunStateProjector(workerCount);
        _store = new JsonFileStore<RunStatePersistenceSnapshot>(stateFilePath, JsonOptions, logger);
        LoadFromDisk();
    }

    /// <summary>
    /// Создает или перезаписывает запись запуска в статусе выполнения.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="targetCodes">Target-коды запуска.</param>
    /// <param name="memberOrScriptNames">Мемберы основной выгрузки или коды скриптов extra.</param>
    /// <param name="publishToGateway">Публиковать ли результаты в шлюз.</param>
    /// <param name="taskCode">Код задачи (<c>run</c> по умолчанию, <c>extra</c> для доп-выгрузки).</param>
    public void SetStarted(string correlationId, IReadOnlyCollection<string> targetCodes, IReadOnlyCollection<string> memberOrScriptNames, bool publishToGateway = true, string taskCode = TaskCodeRun)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(targetCodes);
        ArgumentNullException.ThrowIfNull(memberOrScriptNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskCode);

        var now = DateTimeOffset.UtcNow;
        var snapshot = _projector.CreateStarted(
            correlationId,
            targetCodes,
            memberOrScriptNames,
            publishToGateway,
            taskCode,
            now);

        _runs[correlationId] = snapshot;
        PersistSnapshot();
    }

    /// <summary>
    /// Обновляет запись запуска в статус выполняется.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    public void SetRunning(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var now = DateTimeOffset.UtcNow;
        MutateRun(
            correlationId,
            addFactory: () => _projector.CreateStarted(
                correlationId,
                targetCodes: Array.Empty<string>(),
                memberOrScriptNames: Array.Empty<string>(),
                publishToGateway: true,
                taskCode: TaskCodeRun,
                now),
            updateFactory: current => _projector.UpdateForRunning(current, now));
        PersistSnapshot();
    }

    /// <summary>
    /// Применяет входящее событие раннера к снимку состояния запуска.
    /// </summary>
    /// <param name="event">Событие, на основании которого обновляется статус.</param>
    public void ApplyEvent(RunnerEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentException.ThrowIfNullOrWhiteSpace(@event.CorrelationId);

        var now = DateTimeOffset.UtcNow;
        MutateRun(
            @event.CorrelationId,
            addFactory: () => _projector.CreateFromEvent(@event, now),
            updateFactory: current => _projector.ApplyRunnerEvent(current, @event, now));
        PersistSnapshot();
    }

    public void ApplySenderFeedback(SenderFileDispatchFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback.BatchId);

        var now = DateTimeOffset.UtcNow;
        MutateRun(
            feedback.CorrelationId,
            addFactory: () => _projector.CreateFromSenderFeedback(feedback, now),
            updateFactory: current => _projector.ApplySenderFeedback(current, feedback, now));
        PersistSnapshot();
    }

    /// <summary>
    /// Помечает запуск как завершившийся ошибкой.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="message">Диагностическое сообщение об ошибке.</param>
    public void SetFailed(string correlationId, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var now = DateTimeOffset.UtcNow;
        MutateRun(
            correlationId,
            addFactory: null,
            updateFactory: current => _projector.UpdateToFailed(current, message, now));
        PersistSnapshot();
    }

    /// <summary>
    /// Помечает запуск как ожидающий завершения отмены.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="message">Сообщение о запросе отмены.</param>
    public void SetCancellationRequested(string correlationId, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var now = DateTimeOffset.UtcNow;
        MutateRun(
            correlationId,
            addFactory: null,
            updateFactory: current => _projector.UpdateToCancellationRequested(current, message, now));
        PersistSnapshot();
    }

    /// <summary>
    /// Помечает запуск как отмененный пользователем.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="message">Сообщение об отмене.</param>
    public void SetCancelled(string correlationId, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var now = DateTimeOffset.UtcNow;
        MutateRun(
            correlationId,
            addFactory: null,
            updateFactory: current => _projector.UpdateToCancelled(current, message, now));
        PersistSnapshot();
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

    public int PruneTerminalRuns(DateOnly oldestDayToKeepInclusive)
    {
        var removed = 0;
        foreach (var pair in _runs.ToArray())
        {
            var run = pair.Value;
            if (run.Status is not (RunLifecycleStatus.Completed or RunLifecycleStatus.Failed or RunLifecycleStatus.Cancelled))
            {
                continue;
            }

            var runDay = DateOnly.FromDateTime(run.CreatedAt.LocalDateTime);
            if (runDay >= oldestDayToKeepInclusive)
            {
                continue;
            }

            if (_runs.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            PersistSnapshot();
        }

        return removed;
    }

    private RunStatusInfo MutateRun(
        string correlationId,
        Func<RunStatusInfo>? addFactory,
        Func<RunStatusInfo, RunStatusInfo> updateFactory)
    {
        while (true)
        {
            if (_runs.TryGetValue(correlationId, out var current))
            {
                var updated = updateFactory(current);
                if (ReferenceEquals(updated, current))
                {
                    return current;
                }

                if (_runs.TryUpdate(correlationId, updated, current))
                {
                    return updated;
                }

                continue;
            }

            if (addFactory is null)
            {
                throw new KeyNotFoundException($"Run '{correlationId}' was not found.");
            }

            var created = addFactory();
            if (_runs.TryAdd(correlationId, created))
            {
                return created;
            }
        }
    }

    private void LoadFromDisk()
    {
        var recoveredAt = DateTimeOffset.UtcNow;
        var snapshot = _store.Load();
        if (snapshot?.Runs is null)
        {
            return;
        }

        foreach (var run in snapshot.Runs)
        {
            var normalizedRun = NormalizeRecoveredRun(run, recoveredAt);
            _runs[normalizedRun.CorrelationId] = normalizedRun;
        }
    }

    private void PersistSnapshot()
    {
        var snapshot = new RunStatePersistenceSnapshot(
            PersistenceVersion,
            DateTimeOffset.UtcNow,
            _runs.Values.OrderByDescending(static run => run.UpdatedAt).ToArray());
        _store.Save(snapshot);
    }

    private static RunStatusInfo NormalizeRecoveredRun(RunStatusInfo run, DateTimeOffset recoveredAt)
    {
        if (run.Status is not (RunLifecycleStatus.Running or RunLifecycleStatus.CancellationRequested))
        {
            return run;
        }

        return run with
        {
            Status = RunLifecycleStatus.Cancelled,
            UpdatedAt = recoveredAt,
            LastStep = RunnerStep.Failed,
            Message = "Run was interrupted due to server restart.",
            WorkerStatuses = RunWorkerProjector.Reset(run.WorkerStatuses, recoveredAt)
        };
    }

    private record RunStatePersistenceSnapshot(
        string Version,
        DateTimeOffset SavedAt,
        IReadOnlyCollection<RunStatusInfo> Runs);
}
