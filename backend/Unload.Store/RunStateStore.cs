using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly Regex WorkerIdRegex = new(@"Worker\s*#(?<id>\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private const string TaskCodeRun = "run";
    private const string TaskCodePreset = "preset";
    private const string TaskCodeExtra = "extra";
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
    /// <param name="memberNames">Мемберы, выбранные для выгрузки (для extra — коды скриптов).</param>
    /// <param name="publishToGateway">Публиковать ли результаты в шлюз.</param>
    /// <param name="taskCode">Код задачи (<c>run</c> по умолчанию, <c>extra</c> для доп-выгрузки).</param>
    public void SetStarted(string correlationId, IReadOnlyCollection<string> targetCodes, IReadOnlyCollection<string> memberNames, bool publishToGateway = true, string taskCode = TaskCodeRun)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(targetCodes);
        ArgumentNullException.ThrowIfNull(memberNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskCode);

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
            taskCode,
            RunLifecycleStatus.Running,
            targetCodes.ToArray(),
            now,
            now,
            Message: "Run started.",
            MemberStatuses: memberStatuses,
            OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
            WorkerStatuses: _projector.CreateInitialWorkerStatuses(now),
            SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase),
            PublishToGateway: publishToGateway);

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
        UpsertRun(
            correlationId,
            () => new RunStatusInfo(
                CorrelationId: correlationId,
                TaskCode: TaskCodeRun,
                Status: RunLifecycleStatus.Running,
                PublishToGateway: true,
                TargetCodes: Array.Empty<string>(),
                CreatedAt: now,
                UpdatedAt: now,
                Message: "Run started.",
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
                WorkerStatuses: _projector.CreateInitialWorkerStatuses(now),
                SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)),
            current => _projector.UpdateForRunning(current, now));
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
        UpsertRun(
            @event.CorrelationId,
            () => _projector.CreateFromEvent(@event, now),
            current => _projector.ApplyRunnerEvent(current, @event, now));
        PersistSnapshot();
    }

    public void ApplySenderFeedback(SenderFileDispatchFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback.BatchId);

        var now = DateTimeOffset.UtcNow;
        UpsertRun(
            feedback.CorrelationId,
            () => _projector.CreateFromSenderFeedback(feedback, now),
            current => _projector.ApplySenderFeedback(current, feedback, now));
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
        UpdateExistingRun(correlationId, current => _projector.UpdateToFailed(current, message, now));
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
        UpdateExistingRun(correlationId, current => _projector.UpdateToCancellationRequested(current, message, now));
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
        UpdateExistingRun(correlationId, current => _projector.UpdateToCancelled(current, message, now));
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

    internal static string ResolveTaskCodeByCorrelationId(string correlationId)
    {
        var normalized = correlationId?.Trim() ?? string.Empty;
        if (normalized.StartsWith("extra-", StringComparison.OrdinalIgnoreCase))
        {
            return TaskCodeExtra;
        }

        if (normalized.StartsWith("preset-", StringComparison.OrdinalIgnoreCase))
        {
            return TaskCodePreset;
        }

        return TaskCodeRun;
    }

    private RunStatusInfo UpdateExistingRun(string correlationId, Func<RunStatusInfo, RunStatusInfo> updater)
    {
        while (true)
        {
            if (!_runs.TryGetValue(correlationId, out var current))
            {
                throw new KeyNotFoundException($"Run '{correlationId}' was not found.");
            }

            var updated = updater(current);
            if (ReferenceEquals(updated, current))
            {
                return current;
            }

            if (_runs.TryUpdate(correlationId, updated, current))
            {
                return updated;
            }
        }
    }

    private RunStatusInfo UpsertRun(
        string correlationId,
        Func<RunStatusInfo> addFactory,
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
            WorkerStatuses = RunStateProjector.ResetWorkers(run.WorkerStatuses, recoveredAt)
        };
    }

    private record RunStatePersistenceSnapshot(
        string Version,
        DateTimeOffset SavedAt,
        IReadOnlyCollection<RunStatusInfo> Runs);

    private sealed class RunStateProjector
    {
        private readonly int _workerCount;

        public RunStateProjector(int workerCount)
        {
            _workerCount = Math.Max(1, workerCount);
        }

        public IReadOnlyDictionary<int, RunWorkerStatusInfo> CreateInitialWorkerStatuses(DateTimeOffset now)
        {
            return Enumerable.Range(1, _workerCount)
                .ToDictionary(
                    static workerId => workerId,
                    workerId => new RunWorkerStatusInfo(
                        workerId,
                        "idle",
                        null,
                        null,
                        now));
        }

        public RunStatusInfo CreateFromEvent(RunnerEvent @event, DateTimeOffset now)
        {
            return new RunStatusInfo(
                CorrelationId: @event.CorrelationId,
                TaskCode: TaskCodeRun,
                Status: MapStatus(@event.Step),
                PublishToGateway: true,
                TargetCodes: Array.Empty<string>(),
                CreatedAt: now,
                UpdatedAt: now,
                LastStep: @event.Step,
                Message: @event.Message,
                OutputPath: @event.FilePath,
                MemberStatuses: ApplyMemberEvent(
                    new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                    @event,
                    now),
                OutputArtifacts: ApplyArtifacts(Array.Empty<RunOutputArtifactInfo>(), @event),
                WorkerStatuses: ApplyWorkerEvent(CreateInitialWorkerStatuses(now), @event, now),
                SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase));
        }

        public RunStatusInfo ApplyRunnerEvent(RunStatusInfo current, RunnerEvent @event, DateTimeOffset now)
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

            var updated = current with
            {
                Status = MapStatus(@event.Step),
                UpdatedAt = now,
                LastStep = @event.Step,
                Message = @event.Message,
                OutputPath = @event.Step == RunnerStep.Completed ? @event.FilePath : current.OutputPath,
                MemberStatuses = ApplyMemberEvent(current.MemberStatuses, @event, now),
                OutputArtifacts = ApplyArtifacts(current.OutputArtifacts, @event),
                WorkerStatuses = ApplyWorkerEvent(current.WorkerStatuses, @event, now)
            };

            return TryPromoteToCompleted(updated, now);
        }

        public RunStatusInfo CreateFromSenderFeedback(SenderFileDispatchFeedback feedback, DateTimeOffset now)
        {
            return new RunStatusInfo(
                CorrelationId: feedback.CorrelationId,
                TaskCode: RunStateStore.ResolveTaskCodeByCorrelationId(feedback.CorrelationId),
                Status: RunLifecycleStatus.Running,
                PublishToGateway: true,
                TargetCodes: Array.Empty<string>(),
                CreatedAt: now,
                UpdatedAt: now,
                Message: "Sender feedback received.",
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
                WorkerStatuses: CreateInitialWorkerStatuses(now),
                SenderBatches: ApplySenderFeedbackCore(
                    source: null,
                    feedback,
                    now));
        }

        public RunStatusInfo ApplySenderFeedback(RunStatusInfo current, SenderFileDispatchFeedback feedback, DateTimeOffset now)
        {
            var updated = current with
            {
                UpdatedAt = now,
                SenderBatches = ApplySenderFeedbackCore(current.SenderBatches, feedback, now)
            };
            return TryPromoteToCompleted(updated, now);
        }

        public RunStatusInfo UpdateForRunning(RunStatusInfo current, DateTimeOffset now)
        {
            if (IsTerminalStatus(current.Status))
            {
                return current;
            }

            return current with
            {
                Status = RunLifecycleStatus.Running,
                UpdatedAt = now,
                Message = "Run started.",
                WorkerStatuses = current.WorkerStatuses is null || current.WorkerStatuses.Count == 0
                    ? CreateInitialWorkerStatuses(now)
                    : current.WorkerStatuses
            };
        }

        public RunStatusInfo UpdateToFailed(RunStatusInfo current, string message, DateTimeOffset now)
        {
            return current with
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
                    now),
                WorkerStatuses = ResetWorkers(current.WorkerStatuses, now)
            };
        }

        public RunStatusInfo UpdateToCancellationRequested(RunStatusInfo current, string message, DateTimeOffset now)
        {
            if (IsTerminalStatus(current.Status))
            {
                return current;
            }

            return current with
            {
                Status = RunLifecycleStatus.CancellationRequested,
                UpdatedAt = now,
                Message = message,
                WorkerStatuses = current.WorkerStatuses is null || current.WorkerStatuses.Count == 0
                    ? CreateInitialWorkerStatuses(now)
                    : current.WorkerStatuses
            };
        }

        public RunStatusInfo UpdateToCancelled(RunStatusInfo current, string message, DateTimeOffset now)
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
                    now),
                WorkerStatuses = ResetWorkers(current.WorkerStatuses, now)
            };
        }

        private static RunLifecycleStatus MapStatus(RunnerStep step)
        {
            return step switch
            {
                RunnerStep.Completed => RunLifecycleStatus.Running,
                RunnerStep.Failed => RunLifecycleStatus.Failed,
                _ => RunLifecycleStatus.Running
            };
        }

        private static bool IsTerminalStatus(RunLifecycleStatus status)
        {
            return status is RunLifecycleStatus.Completed or RunLifecycleStatus.Failed or RunLifecycleStatus.Cancelled;
        }

        private static RunStatusInfo TryPromoteToCompleted(RunStatusInfo current, DateTimeOffset now)
        {
            if (current.Status != RunLifecycleStatus.Running)
            {
                return current;
            }

            if (current.LastStep != RunnerStep.Completed)
            {
                return current;
            }

            if (!current.PublishToGateway)
            {
                var batchMap = new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase);
                var memberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (current.MemberStatuses is not null)
                {
                    foreach (var memberName in current.MemberStatuses.Keys)
                    {
                        if (!string.IsNullOrWhiteSpace(memberName))
                        {
                            memberNames.Add(memberName.Trim());
                        }
                    }
                }

                if (current.OutputArtifacts is not null)
                {
                    foreach (var artifact in current.OutputArtifacts)
                    {
                        if (!string.IsNullOrWhiteSpace(artifact.MemberName))
                        {
                            memberNames.Add(artifact.MemberName.Trim());
                        }
                    }
                }

                foreach (var memberName in memberNames)
                {
                    var key = $"skipped:{memberName}";
                    batchMap[key] = new SenderBatchStatusInfo(
                        BatchId: key,
                        MemberName: memberName,
                        Status: SenderBatchStatus.SkippedByRequest,
                        UpdatedAt: now,
                        SentFiles: Array.Empty<SenderFileDispatchStateInfo>(),
                        Message: "Gateway publish skipped by request.");
                }

                return current with
                {
                    Status = RunLifecycleStatus.Completed,
                    UpdatedAt = now,
                    SenderBatches = batchMap
                };
            }

            var senderBatches = current.SenderBatches ?? new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase);
            if (senderBatches.Values.Any(static x => x.Status == SenderBatchStatus.Failed))
            {
                return current with
                {
                    Status = RunLifecycleStatus.Failed,
                    UpdatedAt = now,
                    Message = "Sender batch failed."
                };
            }

            var artifacts = current.OutputArtifacts ?? Array.Empty<RunOutputArtifactInfo>();
            var memberArtifacts = artifacts.Where(static x => !string.IsNullOrWhiteSpace(x.MemberName)).ToArray();
            if (memberArtifacts.Length == 0)
            {
                var allBatchesCompleted = senderBatches.Count == 0 ||
                                         senderBatches.Values.All(static x =>
                                             x.Status is SenderBatchStatus.Completed or SenderBatchStatus.SkippedByRequest);
                return allBatchesCompleted
                    ? current with { Status = RunLifecycleStatus.Completed, UpdatedAt = now }
                    : current;
            }

            foreach (var batch in senderBatches.Values)
            {
                if (batch.Status is not (SenderBatchStatus.Completed or SenderBatchStatus.SkippedByRequest))
                {
                    return current;
                }
            }

            foreach (var artifact in memberArtifacts)
            {
                var memberName = artifact.MemberName!.Trim();
                var batch = senderBatches.Values.FirstOrDefault(x =>
                    string.Equals(x.MemberName, memberName, StringComparison.OrdinalIgnoreCase));
                if (batch is null)
                {
                    return current;
                }

                if (batch.Status == SenderBatchStatus.SkippedByRequest)
                {
                    continue;
                }

                var artifactPath = NormalizePathSafe(artifact.FilePath);
                var sent = (batch.SentFiles ?? Array.Empty<SenderFileDispatchStateInfo>())
                    .Any(x => string.Equals(NormalizePathSafe(x.FilePath), artifactPath, StringComparison.OrdinalIgnoreCase));
                if (!sent)
                {
                    return current;
                }
            }

            return current with { Status = RunLifecycleStatus.Completed, UpdatedAt = now };
        }

        private static string NormalizePathSafe(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
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

            if (@event.Step == RunnerStep.Failed && string.IsNullOrWhiteSpace(@event.MemberName))
            {
                return UpdateAllMemberStatuses(map, MemberRunLifecycleStatus.Failed, @event.Step, @event.Message, now);
            }

            if (string.IsNullOrWhiteSpace(@event.MemberName))
            {
                return map;
            }

            var memberName = @event.MemberName.Trim();
            var status = @event.Step switch
            {
                RunnerStep.Failed => MemberRunLifecycleStatus.Failed,
                RunnerStep.ScriptCompleted => MemberRunLifecycleStatus.Completed,
                _ => MemberRunLifecycleStatus.Running
            };
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

        private static IReadOnlyCollection<RunOutputArtifactInfo> ApplyArtifacts(
            IReadOnlyCollection<RunOutputArtifactInfo>? source,
            RunnerEvent @event)
        {
            var artifacts = source?.ToList() ?? [];
            if (@event.Step != RunnerStep.FileWritten || string.IsNullOrWhiteSpace(@event.FilePath))
            {
                return artifacts;
            }

            if (artifacts.Any(x => string.Equals(x.FilePath, @event.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                return artifacts;
            }

            artifacts.Add(new RunOutputArtifactInfo(
                Path.GetFileName(@event.FilePath),
                @event.FilePath,
                @event.MemberName,
                @event.ScriptCode,
                @event.OccurredAt));
            return artifacts
                .OrderByDescending(static x => x.OccurredAt)
                .ToArray();
        }

        private static IReadOnlyDictionary<int, RunWorkerStatusInfo> ApplyWorkerEvent(
            IReadOnlyDictionary<int, RunWorkerStatusInfo>? source,
            RunnerEvent @event,
            DateTimeOffset now)
        {
            var map = source is null
                ? new Dictionary<int, RunWorkerStatusInfo>()
                : new Dictionary<int, RunWorkerStatusInfo>(source);

            if (@event.Step is RunnerStep.Completed or RunnerStep.Failed)
            {
                return ResetWorkers(map, now);
            }

            var workerId = @event.WorkerId ?? TryExtractWorkerId(@event.Message);
            if (workerId is null)
            {
                return map;
            }

            if (@event.Step == RunnerStep.QueryStarted)
            {
                map[workerId.Value] = new RunWorkerStatusInfo(
                    workerId.Value,
                    "running",
                    @event.ScriptCode,
                    @event.MemberName,
                    now);
                return map;
            }

            if (@event.Step == RunnerStep.QueryCompleted)
            {
                map[workerId.Value] = new RunWorkerStatusInfo(
                    workerId.Value,
                    "idle",
                    null,
                    null,
                    now);
            }

            return map;
        }

        public static IReadOnlyDictionary<int, RunWorkerStatusInfo> ResetWorkers(
            IReadOnlyDictionary<int, RunWorkerStatusInfo>? source,
            DateTimeOffset now)
        {
            if (source is null || source.Count == 0)
            {
                return new Dictionary<int, RunWorkerStatusInfo>();
            }

            return source.ToDictionary(
                static x => x.Key,
                x => x.Value with
                {
                    State = "idle",
                    ScriptCode = null,
                    MemberName = null,
                    UpdatedAt = now
                });
        }

        private static IReadOnlyDictionary<string, SenderBatchStatusInfo> ApplySenderFeedbackCore(
            IReadOnlyDictionary<string, SenderBatchStatusInfo>? source,
            SenderFileDispatchFeedback feedback,
            DateTimeOffset now)
        {
            var map = source is null
                ? new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, SenderBatchStatusInfo>(source, StringComparer.OrdinalIgnoreCase);

            map.TryGetValue(feedback.BatchId, out var currentBatch);
            var sentFiles = currentBatch?.SentFiles?.ToList() ?? [];

            if (feedback.Kind == SenderFeedbackKind.FileSent && !string.IsNullOrWhiteSpace(feedback.FilePath))
            {
                var normalizedPath = NormalizePathSafe(feedback.FilePath);
                if (sentFiles.All(x => !string.Equals(x.FilePath, normalizedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    sentFiles.Add(new SenderFileDispatchStateInfo(normalizedPath, feedback.OccurredAt));
                }
            }

            var status = feedback.Kind switch
            {
                SenderFeedbackKind.FileSent => SenderBatchStatus.InProgress,
                SenderFeedbackKind.BatchCompleted => SenderBatchStatus.Completed,
                SenderFeedbackKind.BatchFailed => SenderBatchStatus.Failed,
                _ => SenderBatchStatus.InProgress
            };

            map[feedback.BatchId] = new SenderBatchStatusInfo(
                BatchId: feedback.BatchId,
                MemberName: feedback.MemberName,
                Status: status,
                UpdatedAt: now,
                SentFiles: sentFiles
                    .OrderBy(static x => x.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Message: feedback.Message);

            return map;
        }

        private static int? TryExtractWorkerId(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var match = WorkerIdRegex.Match(message);
            return match.Success && int.TryParse(match.Groups["id"].Value, out var workerId)
                ? workerId
                : null;
        }
    }
}
