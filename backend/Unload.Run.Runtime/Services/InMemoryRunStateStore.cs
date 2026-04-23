using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Unload.Core;
using Unload.Run.Application;

namespace Unload.Run.Runtime;

/// <summary>
/// Потокобезопасное in-memory хранилище статусов запусков.
/// Используется API и background worker для синхронизации жизненного цикла run.
/// </summary>
public class InMemoryRunStateStore : IRunStateStore
{
    private const string PersistenceVersion = "1";
    private static readonly Regex WorkerIdRegex = new(@"Worker\s*#(?<id>\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private const string TaskCodeRun = "run";
    private readonly int _workerCount;
    private readonly string _stateFilePath;
    private readonly object _persistSync = new();
    private readonly ConcurrentDictionary<string, RunStatusInfo> _runs = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryRunStateStore(
        int workerCount,
        string stateFilePath)
    {
        _workerCount = Math.Max(1, workerCount);
        _stateFilePath = stateFilePath;
        LoadFromDisk();
    }

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
            TaskCodeRun,
            RunLifecycleStatus.Running,
            targetCodes.ToArray(),
            now,
            now,
            Message: "Run started.",
            MemberStatuses: memberStatuses,
            OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
            WorkerStatuses: CreateInitialWorkerStatuses(now),
            SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase));

        _runs[correlationId] = snapshot;
        PersistSnapshot();
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
                TaskCodeRun,
                RunLifecycleStatus.Running,
                Array.Empty<string>(),
                now,
                now,
                Message: "Run started.",
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
                WorkerStatuses: CreateInitialWorkerStatuses(now),
                SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)),
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
                    Message = "Run started.",
                    WorkerStatuses = current.WorkerStatuses is null || current.WorkerStatuses.Count == 0
                        ? CreateInitialWorkerStatuses(now)
                        : current.WorkerStatuses
                };
            });
        PersistSnapshot();
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
                TaskCodeRun,
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
                    now),
                ApplyArtifacts(Array.Empty<RunOutputArtifactInfo>(), @event),
                ApplyWorkerEvent(CreateInitialWorkerStatuses(now), @event, now),
                new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)),
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
            });
        PersistSnapshot();
    }

    public void ApplySenderFeedback(SenderFileDispatchFeedback feedback)
    {
        var now = DateTimeOffset.UtcNow;
        _runs.AddOrUpdate(
            feedback.CorrelationId,
            _ => new RunStatusInfo(
                feedback.CorrelationId,
                TaskCodeRun,
                RunLifecycleStatus.Running,
                Array.Empty<string>(),
                now,
                now,
                Message: "Sender feedback received.",
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
                WorkerStatuses: CreateInitialWorkerStatuses(now),
                SenderBatches: ApplySenderFeedbackCore(
                    source: null,
                    feedback,
                    now)),
            (_, current) =>
            {
                var updated = current with
                {
                    UpdatedAt = now,
                    SenderBatches = ApplySenderFeedbackCore(current.SenderBatches, feedback, now)
                };
                return TryPromoteToCompleted(updated, now);
            });
        PersistSnapshot();
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
                TaskCodeRun,
                RunLifecycleStatus.Failed,
                Array.Empty<string>(),
                now,
                now,
                RunnerStep.Failed,
                message,
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
                WorkerStatuses: CreateInitialWorkerStatuses(now),
                SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)),
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
                    now),
                WorkerStatuses = ResetWorkers(current.WorkerStatuses, now)
            });
        PersistSnapshot();
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
                TaskCodeRun,
                RunLifecycleStatus.CancellationRequested,
                Array.Empty<string>(),
                now,
                now,
                Message: message,
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
                WorkerStatuses: CreateInitialWorkerStatuses(now),
                SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)),
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
                    Message = message,
                    WorkerStatuses = current.WorkerStatuses is null || current.WorkerStatuses.Count == 0
                        ? CreateInitialWorkerStatuses(now)
                        : current.WorkerStatuses
                };
            });
        PersistSnapshot();
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
                TaskCodeRun,
                RunLifecycleStatus.Cancelled,
                Array.Empty<string>(),
                now,
                now,
                RunnerStep.Failed,
                message,
                MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase),
                OutputArtifacts: Array.Empty<RunOutputArtifactInfo>(),
                WorkerStatuses: CreateInitialWorkerStatuses(now),
                SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)),
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
                        now),
                    WorkerStatuses = ResetWorkers(current.WorkerStatuses, now)
                };
            });
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

    private static RunLifecycleStatus MapStatus(RunnerStep step)
    {
        return step switch
        {
            // Run is considered completed only after MQ sender confirms dispatch of all artifacts.
            // Promotion to Completed is handled in TryPromoteToCompleted.
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
            // No per-member artifacts to dispatch. If sender has no pending work, allow completion.
            var allBatchesCompleted = senderBatches.Count == 0 ||
                                     senderBatches.Values.All(static x => x.Status == SenderBatchStatus.Completed);
            return allBatchesCompleted
                ? current with { Status = RunLifecycleStatus.Completed, UpdatedAt = now }
                : current;
        }

        // Require that every artifact filePath appears in sentFiles for its member batch, and that each batch is completed.
        foreach (var batch in senderBatches.Values)
        {
            if (batch.Status != SenderBatchStatus.Completed)
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

    private static IReadOnlyDictionary<int, RunWorkerStatusInfo> ResetWorkers(
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
            var normalizedPath = Path.GetFullPath(feedback.FilePath);
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

    private IReadOnlyDictionary<int, RunWorkerStatusInfo> CreateInitialWorkerStatuses(DateTimeOffset now)
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

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return;
            }

            var json = File.ReadAllText(_stateFilePath);
            var snapshot = JsonSerializer.Deserialize<RunStatePersistenceSnapshot>(json, JsonOptions);
            if (snapshot?.Runs is null || snapshot.Runs.Count == 0)
            {
                return;
            }

            var recoveredAt = DateTimeOffset.UtcNow;
            foreach (var run in snapshot.Runs)
            {
                var normalizedRun = NormalizeRecoveredRun(run, recoveredAt);
                _runs[normalizedRun.CorrelationId] = normalizedRun;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load run state from '{_stateFilePath}': {ex.Message}");
        }
    }

    private void PersistSnapshot()
    {
        try
        {
            lock (_persistSync)
            {
                var directory = Path.GetDirectoryName(_stateFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var snapshot = new RunStatePersistenceSnapshot(
                    PersistenceVersion,
                    DateTimeOffset.UtcNow,
                    _runs.Values.OrderByDescending(static run => run.UpdatedAt).ToArray());
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                var tempPath = $"{_stateFilePath}.tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _stateFilePath, true);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to persist run state to '{_stateFilePath}': {ex.Message}");
        }
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
            WorkerStatuses = ResetWorkers(run.WorkerStatuses, recoveredAt)
        };
    }

    private  record RunStatePersistenceSnapshot(
        string Version,
        DateTimeOffset SavedAt,
        IReadOnlyCollection<RunStatusInfo> Runs);
}
