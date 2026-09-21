using Unload.Core;

namespace Unload.Store;

/// <summary>
/// Строит неизменяемую проекцию прохождения отдельных скриптов.
/// </summary>
internal static class RunScriptProjector
{
    public static IReadOnlyDictionary<string, ScriptRunStatusInfo> Apply(
        IReadOnlyDictionary<string, ScriptRunStatusInfo>? source,
        RunnerEvent @event)
    {
        var map = source is null
            ? new Dictionary<string, ScriptRunStatusInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ScriptRunStatusInfo>(source, StringComparer.OrdinalIgnoreCase);

        if (@event.Step == RunnerStep.Failed && !HasScriptIdentity(@event))
        {
            return FailUnfinished(map, @event.Message, @event.OccurredAt);
        }

        if (!HasScriptIdentity(@event))
        {
            return map;
        }

        var memberName = @event.MemberName!.Trim();
        var scriptCode = @event.ScriptCode!.Trim();
        var id = CreateId(memberName, scriptCode);

        return @event.Step switch
        {
            RunnerStep.ScriptDiscovered => ApplyDiscovered(map, id, memberName, scriptCode, @event),
            RunnerStep.QueryStarted => ApplyStarted(map, id, @event),
            RunnerStep.QueryCompleted => ApplyCompleted(map, id, @event),
            RunnerStep.Failed => ApplyFailed(map, id, @event),
            _ => map
        };
    }

    public static IReadOnlyDictionary<string, ScriptRunStatusInfo> FailUnfinished(
        IReadOnlyDictionary<string, ScriptRunStatusInfo>? source,
        string? message,
        DateTimeOffset now)
    {
        return UpdateUnfinishedCore(source, ScriptRunStage.Failed, message, now);
    }

    public static IReadOnlyDictionary<string, ScriptRunStatusInfo> CancelUnfinished(
        IReadOnlyDictionary<string, ScriptRunStatusInfo>? source,
        string? message,
        DateTimeOffset now)
    {
        return UpdateUnfinishedCore(source, ScriptRunStage.Cancelled, message, now);
    }

    internal static string CreateId(string memberName, string scriptCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptCode);

        var normalizedMemberName = memberName.Trim().ToUpperInvariant();
        var normalizedScriptCode = scriptCode.Trim().ToUpperInvariant();
        return $"member:{normalizedMemberName.Length}:{normalizedMemberName}|script:{normalizedScriptCode.Length}:{normalizedScriptCode}";
    }

    private static IReadOnlyDictionary<string, ScriptRunStatusInfo> ApplyDiscovered(
        Dictionary<string, ScriptRunStatusInfo> map,
        string id,
        string memberName,
        string scriptCode,
        RunnerEvent @event)
    {
        if (map.ContainsKey(id))
        {
            return map;
        }

        map[id] = new ScriptRunStatusInfo(
            id,
            memberName,
            scriptCode,
            ScriptRunStage.AwaitingWorker,
            @event.OccurredAt,
            @event.OccurredAt,
            @event.OccurredAt,
            Message: @event.Message);
        return map;
    }

    private static IReadOnlyDictionary<string, ScriptRunStatusInfo> ApplyStarted(
        Dictionary<string, ScriptRunStatusInfo> map,
        string id,
        RunnerEvent @event)
    {
        if (!map.TryGetValue(id, out var current))
        {
            return map;
        }

        if (current.Stage is ScriptRunStage.Completed or ScriptRunStage.Failed or ScriptRunStage.Cancelled)
        {
            return map;
        }

        if (current.Stage == ScriptRunStage.Running)
        {
            map[id] = current with
            {
                UpdatedAt = @event.OccurredAt,
                WorkerId = @event.WorkerId ?? current.WorkerId,
                Message = @event.Message
            };
            return map;
        }

        map[id] = current with
        {
            Stage = ScriptRunStage.Running,
            StageEnteredAt = @event.OccurredAt,
            UpdatedAt = @event.OccurredAt,
            StartedAt = current.StartedAt ?? @event.OccurredAt,
            WorkerId = @event.WorkerId ?? current.WorkerId,
            Message = @event.Message
        };
        return map;
    }

    private static IReadOnlyDictionary<string, ScriptRunStatusInfo> ApplyCompleted(
        Dictionary<string, ScriptRunStatusInfo> map,
        string id,
        RunnerEvent @event)
    {
        if (!map.TryGetValue(id, out var current))
        {
            return map;
        }

        if (current.Stage is ScriptRunStage.Failed or ScriptRunStage.Cancelled)
        {
            return map;
        }

        if (current.Stage == ScriptRunStage.Completed)
        {
            map[id] = current with
            {
                UpdatedAt = @event.OccurredAt,
                WorkerId = @event.WorkerId ?? current.WorkerId,
                Records = @event.Records ?? current.Records,
                Message = @event.Message
            };
            return map;
        }

        map[id] = current with
        {
            Stage = ScriptRunStage.Completed,
            StageEnteredAt = @event.OccurredAt,
            UpdatedAt = @event.OccurredAt,
            CompletedAt = @event.OccurredAt,
            WorkerId = @event.WorkerId ?? current.WorkerId,
            Records = @event.Records,
            Message = @event.Message
        };
        return map;
    }

    private static IReadOnlyDictionary<string, ScriptRunStatusInfo> ApplyFailed(
        Dictionary<string, ScriptRunStatusInfo> map,
        string id,
        RunnerEvent @event)
    {
        if (!map.TryGetValue(id, out var current) || current.Stage == ScriptRunStage.Completed)
        {
            return map;
        }

        if (current.Stage == ScriptRunStage.Failed)
        {
            map[id] = current with
            {
                UpdatedAt = @event.OccurredAt,
                WorkerId = @event.WorkerId ?? current.WorkerId,
                Message = @event.Message
            };
            return map;
        }

        map[id] = current with
        {
            Stage = ScriptRunStage.Failed,
            StageEnteredAt = @event.OccurredAt,
            UpdatedAt = @event.OccurredAt,
            CompletedAt = @event.OccurredAt,
            WorkerId = @event.WorkerId ?? current.WorkerId,
            Message = @event.Message
        };
        return map;
    }

    private static IReadOnlyDictionary<string, ScriptRunStatusInfo> UpdateUnfinishedCore(
        IReadOnlyDictionary<string, ScriptRunStatusInfo>? source,
        ScriptRunStage terminalStage,
        string? message,
        DateTimeOffset now)
    {
        if (source is null || source.Count == 0)
        {
            return new Dictionary<string, ScriptRunStatusInfo>(StringComparer.OrdinalIgnoreCase);
        }

        return source.ToDictionary(
            static x => x.Key,
            x => x.Value.Stage is ScriptRunStage.Completed or ScriptRunStage.Failed or ScriptRunStage.Cancelled
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

    private static bool HasScriptIdentity(RunnerEvent @event)
    {
        return !string.IsNullOrWhiteSpace(@event.MemberName) &&
               !string.IsNullOrWhiteSpace(@event.ScriptCode);
    }
}
