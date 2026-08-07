using Unload.Core;

namespace Unload.Store;

internal static class RunArtifactProjector
{
    public static IReadOnlyCollection<RunOutputArtifactInfo> Apply(
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
}
