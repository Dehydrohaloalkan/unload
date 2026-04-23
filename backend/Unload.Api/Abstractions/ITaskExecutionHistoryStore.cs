using Unload.Api.Models;

namespace Unload.Api.Abstractions;

public interface ITaskExecutionHistoryStore
{
    TaskRecord Add(
        string taskCode,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string? correlationId,
        string? message,
        int? scriptsExecuted = null,
        int? filesWritten = null,
        string? outputPath = null);

    IReadOnlyList<TaskRecord> List(DateOnly day);

    bool HasRunToday(string taskCode, DateOnly day);
}

