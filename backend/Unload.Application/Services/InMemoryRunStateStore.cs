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
    /// <param name="memberNames">Мемберы, выбранные для выгрузки.</param>
    public void SetStarted(string correlationId, IReadOnlyCollection<string> targetCodes, IReadOnlyCollection<string> memberNames)
    {
        var now = DateTimeOffset.UtcNow;
        var memberStatuses = memberNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static x => x,
                x => new MemberRunStatusInfo(
                    x,
                    MemberRunLifecycleStatus.Pending,
                    LastStep: null,
                    Message: "Awaiting processing.",
                    UpdatedAt: now),
                StringComparer.OrdinalIgnoreCase);
        var snapshot = new RunStatusInfo(
            correlationId,
            RunLifecycleStatus.Running,
            targetCodes.ToArray(),
            now,
            now,
            Message: "Run started.",
            MemberStatuses: memberStatuses);

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
                Message: "Run started.",
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)),
            (_, current) =>
            {
                if (IsTerminalStatus(current.Status))
                {
                    return current;
                }

                return current with
                {
                    Status = RunLifecycleStatus.Running,
                    UpdatedAt = now,
                    Message = "Run started."
                };
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
                @event.FilePath,
                ApplyMemberEvent(
                    new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                    @event,
                    now)),
            (_, current) =>
            {
                if (IsTerminalStatus(current.Status))
                {
                    return current;
                }

                if (current.Status == RunLifecycleStatus.CancellationRequested &&
                    @event.Step is not RunnerStep.Completed and not RunnerStep.Failed)
                {
                    return current;
                }

                return current with
                {
                    Status = MapStatus(@event.Step),
                    UpdatedAt = now,
                    LastStep = @event.Step,
                    Message = @event.Message,
                    OutputPath = @event.Step == RunnerStep.Completed ? @event.FilePath : current.OutputPath,
                    MemberStatuses = ApplyMemberEvent(current.MemberStatuses, @event, now)
                };
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
                message,
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)),
            (_, current) => current with
            {
                Status = RunLifecycleStatus.Failed,
                UpdatedAt = now,
                LastStep = RunnerStep.Failed,
                Message = message,
                MemberStatuses = UpdateAllMemberStatuses(
                    current.MemberStatuses,
                    MemberRunLifecycleStatus.Failed,
                    RunnerStep.Failed,
                    message,
                    now)
            });
    }

    /// <summary>
    /// Помечает запуск как ожидающий завершения отмены.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="message">Сообщение о запросе отмены.</param>
    public void SetCancellationRequested(string correlationId, string message)
    {
        var now = DateTimeOffset.UtcNow;
        _runs.AddOrUpdate(
            correlationId,
            _ => new RunStatusInfo(
                correlationId,
                RunLifecycleStatus.CancellationRequested,
                Array.Empty<string>(),
                now,
                now,
                Message: message,
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)),
            (_, current) =>
            {
                if (IsTerminalStatus(current.Status))
                {
                    return current;
                }

                return current with
                {
                    Status = RunLifecycleStatus.CancellationRequested,
                    UpdatedAt = now,
                    Message = message
                };
            });
    }

    /// <summary>
    /// Помечает запуск как отмененный пользователем.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="message">Сообщение об отмене.</param>
    public void SetCancelled(string correlationId, string message)
    {
        var now = DateTimeOffset.UtcNow;
        _runs.AddOrUpdate(
            correlationId,
            _ => new RunStatusInfo(
                correlationId,
                RunLifecycleStatus.Cancelled,
                Array.Empty<string>(),
                now,
                now,
                RunnerStep.Failed,
                message,
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)),
            (_, current) =>
            {
                if (IsTerminalStatus(current.Status))
                {
                    return current;
                }

                return current with
                {
                    Status = RunLifecycleStatus.Cancelled,
                    UpdatedAt = now,
                    LastStep = RunnerStep.Failed,
                    Message = message,
                    MemberStatuses = UpdateAllMemberStatuses(
                        current.MemberStatuses,
                        MemberRunLifecycleStatus.Cancelled,
                        RunnerStep.Failed,
                        message,
                        now)
                };
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

    private static bool IsTerminalStatus(RunLifecycleStatus status)
    {
        return status is RunLifecycleStatus.Completed or RunLifecycleStatus.Failed or RunLifecycleStatus.Cancelled;
    }

    private static IReadOnlyDictionary<string, MemberRunStatusInfo> ApplyMemberEvent(
        IReadOnlyDictionary<string, MemberRunStatusInfo>? source,
        RunnerEvent @event,
        DateTimeOffset now)
    {
        var map = source is null
            ? new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, MemberRunStatusInfo>(source, StringComparer.OrdinalIgnoreCase);

        if (@event.Step == RunnerStep.Completed)
        {
            return UpdateAllMemberStatuses(map, MemberRunLifecycleStatus.Completed, @event.Step, @event.Message, now);
        }

        if (@event.Step == RunnerStep.Failed)
        {
            if (string.IsNullOrWhiteSpace(@event.MemberName))
            {
                return UpdateAllMemberStatuses(map, MemberRunLifecycleStatus.Failed, @event.Step, @event.Message, now);
            }
        }

        if (string.IsNullOrWhiteSpace(@event.MemberName))
        {
            return map;
        }

        var memberName = @event.MemberName.Trim();
        var status = @event.Step == RunnerStep.Failed
            ? MemberRunLifecycleStatus.Failed
            : MemberRunLifecycleStatus.Running;
        map[memberName] = new MemberRunStatusInfo(
            memberName,
            status,
            @event.Step,
            @event.Message,
            now);

        return map;
    }

    private static IReadOnlyDictionary<string, MemberRunStatusInfo> UpdateAllMemberStatuses(
        IReadOnlyDictionary<string, MemberRunStatusInfo>? source,
        MemberRunLifecycleStatus status,
        RunnerStep step,
        string? message,
        DateTimeOffset now)
    {
        if (source is null || source.Count == 0)
        {
            return new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase);
        }

        return source.ToDictionary(
            static x => x.Key,
            x => x.Value with
            {
                Status = status,
                LastStep = step,
                Message = message,
                UpdatedAt = now
            },
            StringComparer.OrdinalIgnoreCase);
    }
}
