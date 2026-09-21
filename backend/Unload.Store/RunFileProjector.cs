using Unload.Core;

namespace Unload.Store;

/// <summary>
/// Строит неизменяемую проекцию прохождения чанков через writer.
/// </summary>
internal static class RunFileProjector
{
    public static IReadOnlyDictionary<string, FileRunStatusInfo> Apply(
        IReadOnlyDictionary<string, FileRunStatusInfo>? source,
        RunnerEvent @event)
    {
        var map = source is null
            ? new Dictionary<string, FileRunStatusInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, FileRunStatusInfo>(source, StringComparer.OrdinalIgnoreCase);

        if (@event.Step == RunnerStep.Failed && !HasFileIdentity(@event))
        {
            return FailUnfinished(map, @event.Message, @event.OccurredAt);
        }

        if (!HasFileIdentity(@event))
        {
            return map;
        }

        var memberName = @event.MemberName!.Trim();
        var scriptCode = @event.ScriptCode!.Trim();
        var chunkNumber = @event.ChunkNumber!.Value;
        var id = CreateId(memberName, scriptCode, chunkNumber);

        return @event.Step switch
        {
            RunnerStep.FileWriteStarted => ApplyWriteStarted(map, id, memberName, scriptCode, chunkNumber, @event),
            RunnerStep.FileWritten => ApplyWritten(map, id, memberName, scriptCode, chunkNumber, @event),
            RunnerStep.Failed => ApplyFailed(map, id, @event),
            _ => map
        };
    }

    public static IReadOnlyDictionary<string, FileRunStatusInfo> FailUnfinished(
        IReadOnlyDictionary<string, FileRunStatusInfo>? source,
        string? message,
        DateTimeOffset now) => UpdateUnfinishedCore(source, FileRunStage.Failed, message, now);

    public static IReadOnlyDictionary<string, FileRunStatusInfo> CancelUnfinished(
        IReadOnlyDictionary<string, FileRunStatusInfo>? source,
        string? message,
        DateTimeOffset now) => UpdateUnfinishedCore(source, FileRunStage.Cancelled, message, now);

    internal static string CreateId(string memberName, string scriptCode, int chunkNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptCode);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkNumber);

        var parentScriptId = RunScriptProjector.CreateId(memberName, scriptCode);
        return $"{parentScriptId}|chunk:{chunkNumber}";
    }

    private static IReadOnlyDictionary<string, FileRunStatusInfo> ApplyWriteStarted(
        Dictionary<string, FileRunStatusInfo> map,
        string id,
        string memberName,
        string scriptCode,
        int chunkNumber,
        RunnerEvent @event)
    {
        if (!map.TryGetValue(id, out var current))
        {
            map[id] = new FileRunStatusInfo(
                id,
                RunScriptProjector.CreateId(memberName, scriptCode),
                memberName,
                scriptCode,
                chunkNumber,
                FileRunStage.QueuedForWrite,
                @event.OccurredAt,
                @event.OccurredAt,
                @event.OccurredAt,
                @event.OccurredAt,
                WorkerId: @event.WorkerId,
                Rows: @event.Records,
                EstimatedBytes: @event.EstimatedBytes,
                Message: @event.Message);
            return map;
        }

        if (IsTerminal(current.Stage))
        {
            return map;
        }

        map[id] = current with
        {
            UpdatedAt = @event.OccurredAt,
            WorkerId = @event.WorkerId ?? current.WorkerId,
            Rows = @event.Records ?? current.Rows,
            EstimatedBytes = @event.EstimatedBytes ?? current.EstimatedBytes,
            Message = @event.Message
        };
        return map;
    }

    private static IReadOnlyDictionary<string, FileRunStatusInfo> ApplyWritten(
        Dictionary<string, FileRunStatusInfo> map,
        string id,
        string memberName,
        string scriptCode,
        int chunkNumber,
        RunnerEvent @event)
    {
        if (!map.TryGetValue(id, out var current))
        {
            current = new FileRunStatusInfo(
                id,
                RunScriptProjector.CreateId(memberName, scriptCode),
                memberName,
                scriptCode,
                chunkNumber,
                FileRunStage.QueuedForWrite,
                @event.OccurredAt,
                @event.OccurredAt,
                @event.OccurredAt,
                @event.OccurredAt);
        }

        if (current.Stage is FileRunStage.Failed or FileRunStage.Cancelled)
        {
            return map;
        }

        var fileName = string.IsNullOrWhiteSpace(@event.FilePath)
            ? current.FileName
            : Path.GetFileName(@event.FilePath);
        if (current.Stage == FileRunStage.Written)
        {
            map[id] = current with
            {
                UpdatedAt = @event.OccurredAt,
                WorkerId = @event.WorkerId ?? current.WorkerId,
                Rows = @event.Records ?? current.Rows,
                EstimatedBytes = @event.EstimatedBytes ?? current.EstimatedBytes,
                FileName = fileName,
                FilePath = @event.FilePath ?? current.FilePath,
                Message = @event.Message
            };
            return map;
        }

        map[id] = current with
        {
            Stage = FileRunStage.Written,
            StageEnteredAt = @event.OccurredAt,
            UpdatedAt = @event.OccurredAt,
            CompletedAt = @event.OccurredAt,
            WorkerId = @event.WorkerId ?? current.WorkerId,
            Rows = @event.Records ?? current.Rows,
            EstimatedBytes = @event.EstimatedBytes ?? current.EstimatedBytes,
            FileName = fileName,
            FilePath = @event.FilePath ?? current.FilePath,
            Message = @event.Message
        };
        return map;
    }

    private static IReadOnlyDictionary<string, FileRunStatusInfo> ApplyFailed(
        Dictionary<string, FileRunStatusInfo> map,
        string id,
        RunnerEvent @event)
    {
        if (!map.TryGetValue(id, out var current) || IsTerminal(current.Stage))
        {
            return map;
        }

        map[id] = current with
        {
            Stage = FileRunStage.Failed,
            StageEnteredAt = @event.OccurredAt,
            UpdatedAt = @event.OccurredAt,
            CompletedAt = @event.OccurredAt,
            WorkerId = @event.WorkerId ?? current.WorkerId,
            Message = @event.Message
        };
        return map;
    }

    private static IReadOnlyDictionary<string, FileRunStatusInfo> UpdateUnfinishedCore(
        IReadOnlyDictionary<string, FileRunStatusInfo>? source,
        FileRunStage terminalStage,
        string? message,
        DateTimeOffset now)
    {
        if (source is null || source.Count == 0)
        {
            return new Dictionary<string, FileRunStatusInfo>(StringComparer.OrdinalIgnoreCase);
        }

        return source.ToDictionary(
            static x => x.Key,
            x => IsTerminal(x.Value.Stage)
                ? x.Value
                : x.Value with
                {
                    Stage = terminalStage,
                    StageEnteredAt = now,
                    UpdatedAt = now,
                    CompletedAt = now,
                    Message = message
                },
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasFileIdentity(RunnerEvent @event)
    {
        return !string.IsNullOrWhiteSpace(@event.MemberName) &&
               !string.IsNullOrWhiteSpace(@event.ScriptCode) &&
               @event.ChunkNumber is > 0;
    }

    private static bool IsTerminal(FileRunStage stage)
    {
        return stage is FileRunStage.Written or FileRunStage.Failed or FileRunStage.Cancelled;
    }
}
