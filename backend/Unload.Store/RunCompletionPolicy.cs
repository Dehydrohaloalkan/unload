using Unload.Core;

namespace Unload.Store;

/// <summary>
/// Чистое правило terminal-перехода после завершения runner и обработки gateway feedback.
/// </summary>
internal static class RunCompletionPolicy
{
    public static RunStatusInfo Apply(RunStatusInfo current, DateTimeOffset now)
    {
        if (current.Status != RunLifecycleStatus.Running || current.LastStep != RunnerStep.Completed)
        {
            return current;
        }

        if (!current.PublishToGateway)
        {
            return CompleteWithoutGateway(current, now);
        }

        var senderBatches = current.SenderBatches ??
                            new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase);
        if (senderBatches.Values.Any(static batch => batch.Status == SenderBatchStatus.Failed))
        {
            return current with
            {
                Status = RunLifecycleStatus.Failed,
                UpdatedAt = now,
                Message = "Sender batch failed."
            };
        }

        var artifacts = current.OutputArtifacts ?? Array.Empty<RunOutputArtifactInfo>();
        var memberArtifacts = artifacts
            .Where(static artifact => !string.IsNullOrWhiteSpace(artifact.MemberName))
            .ToArray();
        if (memberArtifacts.Length == 0)
        {
            var allBatchesCompleted = senderBatches.Count == 0 ||
                                      senderBatches.Values.All(static batch =>
                                          batch.Status is SenderBatchStatus.Completed or
                                              SenderBatchStatus.SkippedByRequest);
            return allBatchesCompleted
                ? current with { Status = RunLifecycleStatus.Completed, UpdatedAt = now }
                : current;
        }

        if (senderBatches.Values.Any(static batch =>
                batch.Status is not (SenderBatchStatus.Completed or SenderBatchStatus.SkippedByRequest)))
        {
            return current;
        }

        foreach (var artifact in memberArtifacts)
        {
            var memberName = artifact.MemberName!.Trim();
            var batch = senderBatches.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.MemberName, memberName, StringComparison.OrdinalIgnoreCase));
            if (batch is null)
            {
                return current;
            }

            if (batch.Status == SenderBatchStatus.SkippedByRequest)
            {
                continue;
            }

            var artifactPath = NormalizePathSafe(artifact.FilePath);
            var sent = (batch.SentFiles ?? Array.Empty<SenderFileDispatchStateInfo>())
                .Any(file => string.Equals(
                    NormalizePathSafe(file.FilePath),
                    artifactPath,
                    StringComparison.OrdinalIgnoreCase));
            if (!sent)
            {
                return current;
            }
        }

        return current with { Status = RunLifecycleStatus.Completed, UpdatedAt = now };
    }

    private static RunStatusInfo CompleteWithoutGateway(RunStatusInfo current, DateTimeOffset now)
    {
        var batchMap = new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase);
        var memberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (current.MemberStatuses is not null)
        {
            foreach (var memberName in current.MemberStatuses.Keys)
            {
                if (!string.IsNullOrWhiteSpace(memberName))
                {
                    memberNames.Add(memberName.Trim());
                }
            }
        }

        if (current.OutputArtifacts is not null)
        {
            foreach (var artifact in current.OutputArtifacts)
            {
                if (!string.IsNullOrWhiteSpace(artifact.MemberName))
                {
                    memberNames.Add(artifact.MemberName.Trim());
                }
            }
        }

        foreach (var memberName in memberNames)
        {
            var key = $"skipped:{memberName}";
            batchMap[key] = new SenderBatchStatusInfo(
                BatchId: key,
                MemberName: memberName,
                Status: SenderBatchStatus.SkippedByRequest,
                UpdatedAt: now,
                SentFiles: Array.Empty<SenderFileDispatchStateInfo>(),
                Message: "Gateway publish skipped by request.");
        }

        return current with
        {
            Status = RunLifecycleStatus.Completed,
            UpdatedAt = now,
            SenderBatches = batchMap
        };
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
}
