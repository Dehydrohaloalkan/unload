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
            return UpdateAll(map, MemberRunLifecycleStatus.Failed, @event.Step, @event.Message, now);
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

    public static IReadOnlyDictionary<string, MemberRunStatusInfo> UpdateAll(
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
