using System.Text.Json;
using Unload.Bootstrapper;

namespace Unload.Api;

public  class TaskExecutionHistoryStore : ITaskExecutionHistoryStore
{
    private const string PersistenceVersion = "1";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly ILogger<TaskExecutionHistoryStore> _logger;
    private readonly List<TaskRecord> _records;

    public TaskExecutionHistoryStore(UnloadRuntimePaths runtimePaths, ILogger<TaskExecutionHistoryStore> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(runtimePaths.OutputDirectory, "_state", "task-history.json");
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

    private List<TaskRecord> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var json = File.ReadAllText(_filePath);
            var snapshot = JsonSerializer.Deserialize<TaskHistorySnapshot>(json, JsonOptions);
            return snapshot?.Records?.ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load task history from '{FilePath}'.", _filePath);
            return [];
        }
    }

    private void PersistLocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var snapshot = new TaskHistorySnapshot(PersistenceVersion, DateTimeOffset.UtcNow, _records.ToArray());
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var tempPath = $"{_filePath}.tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist task history to '{FilePath}'.", _filePath);
        }
    }

    private  record TaskHistorySnapshot(
        string Version,
        DateTimeOffset SavedAt,
        IReadOnlyCollection<TaskRecord> Records);
}
