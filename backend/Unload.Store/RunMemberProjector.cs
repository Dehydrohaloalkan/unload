using Unload.Core;

namespace Unload.Store;

internal static class RunMemberProjector
{
    public static IReadOnlyDictionary<string, MemberRunStatusInfo> Apply(
        IReadOnlyDictionary<string, MemberRunStatusInfo>? source,
        RunnerEvent @event,
        DateTimeOffset now)
    {
        var map = source is null
            ? new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, MemberRunStatusInfo>(source, StringComparer.OrdinalIgnoreCase);

        if (@event.Step == RunnerStep.Completed)
        {
            return UpdateAll(map, MemberRunLifecycleStatus.Completed, @event.Step, @event.Message, now);
        }

        if (@event.Step == RunnerStep.Failed && string.IsNullOrWhiteSpace(@event.MemberName))
        {
            return UpdateUnfinishedAsFailed(map, @event.Message, now, @event.Failure);
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
        map.TryGetValue(memberName, out var existing);
        map[memberName] = new MemberRunStatusInfo(
            memberName,
            status,
            @event.Step,
            @event.Message,
            now,
            QueuePosition: existing?.QueuePosition,
            Sequence: @event.Sequence > 0 ? @event.Sequence : existing?.Sequence,
            Failure: @event.Step == RunnerStep.Failed ? @event.Failure : existing?.Failure);

        return map;
    }

    public static IReadOnlyDictionary<string, MemberRunStatusInfo> UpdateAll(
        IReadOnlyDictionary<string, MemberRunStatusInfo>? source,
        MemberRunLifecycleStatus status,
        RunnerStep step,
        string? message,
        DateTimeOffset now,
        RunnerFailureInfo? failure = null)
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
                UpdatedAt = now,
                Failure = failure,
                Sequence = x.Value.Sequence
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, MemberRunStatusInfo> UpdateUnfinishedAsFailed(
        IReadOnlyDictionary<string, MemberRunStatusInfo> source,
        string? message,
        DateTimeOffset now,
        RunnerFailureInfo? failure = null)
    {
        return source.ToDictionary(
            static x => x.Key,
            x => x.Value.Status == MemberRunLifecycleStatus.Completed
                ? x.Value
                : x.Value with
                {
                    Status = MemberRunLifecycleStatus.Failed,
                    LastStep = RunnerStep.Failed,
                    Message = message,
                    UpdatedAt = now,
                    Failure = failure,
                    Sequence = x.Value.Sequence
                },
            StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, MemberRunStatusInfo> ApplyFailure(
        IReadOnlyDictionary<string, MemberRunStatusInfo>? source,
        string memberName,
        RunnerFailureInfo failure,
        DateTimeOffset now)
    {
        var map = source is null
            ? new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, MemberRunStatusInfo>(source, StringComparer.OrdinalIgnoreCase);
        var normalized = memberName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return map;
        }

        map[normalized] = map.TryGetValue(normalized, out var current)
            ? current with
            {
                Status = MemberRunLifecycleStatus.Failed,
                LastStep = RunnerStep.Failed,
                Message = failure.Message,
                UpdatedAt = now,
                Failure = failure
            }
            : new MemberRunStatusInfo(
                normalized,
                MemberRunLifecycleStatus.Failed,
                RunnerStep.Failed,
                failure.Message,
                now,
                Failure: failure);
        return map;
    }
}
