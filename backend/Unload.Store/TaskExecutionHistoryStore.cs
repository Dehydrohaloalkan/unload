using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Unload.Store;

public class TaskExecutionHistoryStore
{
    private const string PersistenceVersion = "1";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly ILogger<TaskExecutionHistoryStore>? _logger;
    private readonly JsonFileStore<TaskHistorySnapshot> _store;
    private readonly List<TaskRecord> _records;

    public TaskExecutionHistoryStore(string filePath, ILogger<TaskExecutionHistoryStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _logger = logger;
        _store = new JsonFileStore<TaskHistorySnapshot>(filePath, JsonOptions, logger);
        _records = Load();
    }

    public TaskRecord Add(
        string taskCode,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string? correlationId,
        string? message,
        int? scriptsExecuted = null,
        int? filesWritten = null,
        string? outputPath = null)
    {
        var normalizedTaskCode = string.IsNullOrWhiteSpace(taskCode) ? "unknown" : taskCode.Trim().ToLowerInvariant();
        var record = new TaskRecord(
            normalizedTaskCode,
            startedAt,
            completedAt,
            correlationId,
            message,
            scriptsExecuted,
            filesWritten,
            outputPath);
        lock (_sync)
        {
            _records.Add(record);
            PersistLocked();
        }

        return record;
    }

    public IReadOnlyList<TaskRecord> List(DateOnly day)
    {
        lock (_sync)
        {
            return _records
                .Where(record => DateOnly.FromDateTime(record.CompletedAt.LocalDateTime) == day)
                .OrderByDescending(static record => record.CompletedAt)
                .ToArray();
        }
    }

    public IReadOnlyList<TaskRecord> ListRange(DateOnly fromInclusive, DateOnly toInclusive)
    {
        if (toInclusive < fromInclusive)
        {
            return Array.Empty<TaskRecord>();
        }

        lock (_sync)
        {
            return _records
                .Where(record =>
                {
                    var day = DateOnly.FromDateTime(record.CompletedAt.LocalDateTime);
                    return day >= fromInclusive && day <= toInclusive;
                })
                .OrderByDescending(static record => record.CompletedAt)
                .ToArray();
        }
    }

    public TaskRecord? TryGetByCorrelationId(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return null;
        }

        var normalized = correlationId.Trim();
        lock (_sync)
        {
            return _records
                .Where(record => !string.IsNullOrWhiteSpace(record.CorrelationId) &&
                                 string.Equals(record.CorrelationId.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(static record => record.CompletedAt)
                .FirstOrDefault();
        }
    }

    public bool HasRunToday(string taskCode, DateOnly day)
    {
        var normalizedTaskCode = string.IsNullOrWhiteSpace(taskCode) ? "unknown" : taskCode.Trim().ToLowerInvariant();
        lock (_sync)
        {
            return _records.Any(record =>
                string.Equals(record.TaskCode, normalizedTaskCode, StringComparison.OrdinalIgnoreCase) &&
                DateOnly.FromDateTime(record.CompletedAt.LocalDateTime) == day);
        }
    }

    public int Prune(DateOnly oldestDayToKeepInclusive)
    {
        lock (_sync)
        {
            var beforeCount = _records.Count;
            _records.RemoveAll(record => DateOnly.FromDateTime(record.CompletedAt.LocalDateTime) < oldestDayToKeepInclusive);
            var removed = beforeCount - _records.Count;
            if (removed > 0)
            {
                PersistLocked();
            }

            return removed;
        }
    }

    private List<TaskRecord> Load()
    {
        var snapshot = _store.Load();
        return snapshot?.Records?.ToList() ?? [];
    }

    private void PersistLocked()
    {
        var snapshot = new TaskHistorySnapshot(PersistenceVersion, DateTimeOffset.UtcNow, _records.ToArray());
        _store.Save(snapshot);
    }

    private record TaskHistorySnapshot(
        string Version,
        DateTimeOffset SavedAt,
        IReadOnlyCollection<TaskRecord> Records);
}
