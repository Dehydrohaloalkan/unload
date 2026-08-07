using System.Text.RegularExpressions;
using Unload.Core;

namespace Unload.Store;

internal sealed class RunWorkerProjector
{
    private static readonly Regex WorkerIdRegex = new(
        @"Worker\s*#(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly int _workerCount;

    public RunWorkerProjector(int workerCount)
    {
        _workerCount = Math.Max(1, workerCount);
    }

    public IReadOnlyDictionary<int, RunWorkerStatusInfo> CreateInitial(DateTimeOffset now)
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

    public IReadOnlyDictionary<int, RunWorkerStatusInfo> Apply(
        IReadOnlyDictionary<int, RunWorkerStatusInfo>? source,
        RunnerEvent @event,
        DateTimeOffset now)
    {
        var map = source is null
            ? new Dictionary<int, RunWorkerStatusInfo>()
            : new Dictionary<int, RunWorkerStatusInfo>(source);

        if (@event.Step is RunnerStep.Completed or RunnerStep.Failed)
        {
            return Reset(map, now);
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

    public static IReadOnlyDictionary<int, RunWorkerStatusInfo> Reset(
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
